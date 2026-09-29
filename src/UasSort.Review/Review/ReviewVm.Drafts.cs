// src/UasSort.Review/Review/ReviewVm.Drafts.cs
namespace UasSort.Review;

public static class DraftOffers
{
    /// <summary>A draft is offered whenever its DraftKey matches this card, even if the InventoryHash differs (Ref §9.11).</summary>
    public static DraftOffer? Find(PlanBase b, IDraftStore store, IPlanDeriver deriver, TimeProvider? clock = null)
    {
        var key = b.Scan.Inventory.Source.DraftKey;
        if (store.Load(key) is not { } draft || draft.Edits.Length == 0) return null;
        var session = PlanSession.Resume(b, draft, deriver, clock ?? TimeProvider.System, out var dropped);
        return new DraftOffer(draft, session, dropped);
    }
}

public sealed partial class ReviewVm
{
    public static readonly TimeSpan DraftDelay = TimeSpan.FromSeconds(1);

    private ITimer? _draftTimer;

    public DraftOffer? Offer { get; private set; }

    public string DraftKey => Plan.Base.Scan.Inventory.Source.DraftKey;

    public Task ResumeDraftAsync()
    {
        if (Offer is not { } o) return Task.CompletedTask;
        Offer = null;
        Detach(Session);          // plans the old session still delivers (an edit deriving right now) are dropped by OnPlanArrived
        Session = o.Session;
        Attach(Session);
        UpdateUndo();
        Tuning.SetCommitted(Session.Current.Tuning);
        Apply(Session.Current);
        OnEditCommitted();
        return Task.CompletedTask;
    }

    public Task DiscardDraftAsync()
    {
        if (Offer is null) return Task.CompletedTask;
        Offer = null;
        _s.Drafts.Delete(DraftKey);
        RebuildInfoBars();
        return Task.CompletedTask;
    }

    internal void SaveDraftNow()
    {
        _draftTimer?.Dispose();
        _draftTimer = null;
        _s.Drafts.Save(DraftKey, Session.ToDraft());
    }

    partial void InitDraftOffer(DraftOffer? offer) => Offer = offer;

    partial void AddDraftInfoBar(List<InfoBarVm> bars)
    {
        if (Offer is not { } o) return;
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(o.Draft.SavedUtc, DateTimeKind.Utc), _s.Time.LocalTimeZone);
        var total = o.Draft.Edits.Length;
        var text = string.Create(CultureInfo.InvariantCulture, $"Resume edits from {Fmt.Clock(local)}? {total - o.Dropped} of {total} still apply");
        bars.Add(new InfoBarVm("draft", InfoSeverity.Informational, text, false,
                               [new QuickFixVm("Resume", ResumeDraftAsync), new QuickFixVm("Discard", DiscardDraftAsync)]));
    }

    /// <summary>Every committed change (edit, quick fix, slider commit, undo, redo, resume) restarts the 1 s draft timer (Ref §9.11).</summary>
    partial void OnEditCommitted()
    {
        _draftTimer?.Dispose();
        _draftTimer = _s.Time.CreateTimer(_ => _s.Ui.Post(SaveDraftNow), null, DraftDelay, Timeout.InfiniteTimeSpan);
    }

    partial void DisposeDrafts()
    {
        _draftTimer?.Dispose();
        _draftTimer = null;
    }
}
