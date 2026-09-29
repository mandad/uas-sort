// tests/UasSort.Review.Tests/DraftTests.cs
namespace UasSort.Review.Tests;

public class DraftTests
{
    private static readonly ItemId Council0 = TestPlans.Id(TestPlans.CouncilAnvil()[0].Name);
    private static readonly ItemId Council1 = TestPlans.Id(TestPlans.CouncilAnvil()[1].Name);
    private static readonly ItemId AnvilFirst = TestPlans.Id(TestPlans.CouncilAnvil()[2].Name);

    [Fact]
    public async Task Drafts_AutosaveOneSecondAfterTheLastChange()
    {
        using var h = ReviewHarness.Create(TestPlans.CouncilAnvil());
        await h.SettleAsync();

        await h.Vm.SplitBeforeAsync(AnvilFirst);
        await h.SettleAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(600));
        await h.Vm.SetIncludedAsync([Council1], false);
        await h.SettleAsync();
        h.Time.Advance(TimeSpan.FromMilliseconds(999));
        h.Ui.RunAll();
        Assert.Equal(0, h.Drafts.Saves);

        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        h.Ui.RunAll();
        Assert.Equal(1, h.Drafts.Saves);
        var draft = h.Drafts.Drafts[TestPlans.Source.DraftKey];
        Assert.Equal(2, draft.Edits.Length);
        Assert.Equal(TestPlans.Source.DraftKey, h.Vm.DraftKey);
    }

    [Fact]
    public async Task Drafts_ChangedInventory_OfferedWithDroppedCountAndPinWarning()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var drafts = new FakeDraftStore();
        drafts.Save(TestPlans.Source.DraftKey, new Draft(1, TestPlans.Source.DraftKey, "0000000000000000",
            TestPlans.Utc(2026, 9, 28, 1, 2), new Tuning(),
            [new Rename(Council0, "Council Road", [Council0, Council1]), new SplitBefore(new ItemId("DCIM/DJI_001/DJI_20260725180000_0099_D.MP4"))]));

        var offer = DraftOffers.Find(b, drafts, new ScriptedDeriver());
        Assert.NotNull(offer);
        Assert.Equal(1, offer!.Dropped);

        using var h = ReviewHarness.Create([], planBase: b, offer: offer, drafts: drafts);
        await h.SettleAsync();
        var bar = Assert.Single(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Equal("Resume edits from 17:02? 1 of 2 still apply", bar.Message);

        await bar.Actions.Single(a => a.Label == "Resume").Command.ExecuteAsync(null);
        await h.SettleAsync();
        Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Equal("Council Road", h.Vm.Plan.Groups[0].Description);
        Assert.Contains(h.Vm.Plan.Issues, i => i.Code == IssueCode.PinMembershipChanged && i.RequiresAckAtPreflight);
        Assert.Contains(h.Card(0).Chips, c => c.Kind == ChipKind.PinChanged);
    }

    [Fact]
    public async Task Drafts_Discard_DeletesTheDraft()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var drafts = new FakeDraftStore();
        drafts.Save(TestPlans.Source.DraftKey, new Draft(1, TestPlans.Source.DraftKey, b.Scan.Inventory.InventoryHash,
            TestPlans.Utc(2026, 9, 28, 1, 2), new Tuning(), [new SplitBefore(AnvilFirst)]));
        using var h = ReviewHarness.Create([], planBase: b, offer: DraftOffers.Find(b, drafts, new ScriptedDeriver()), drafts: drafts);
        await h.SettleAsync();

        await h.Vm.InfoBars.Single(i => i.Key == "draft").Actions.Single(a => a.Label == "Discard").Command.ExecuteAsync(null);

        Assert.Contains(TestPlans.Source.DraftKey, drafts.Deleted);
        Assert.DoesNotContain(h.Vm.InfoBars, i => i.Key == "draft");
        Assert.Single(h.Vm.Plan.Groups);
    }

    [Fact]
    public void Drafts_NoDraftForThisCard_NoOffer()
        => Assert.Null(DraftOffers.Find(TestPlans.Base(TestPlans.CouncilAnvil()), new FakeDraftStore(), new ScriptedDeriver()));

    [Fact]
    public async Task Drafts_ResumeWhileAnEditIsDeriving_KeepsTheResumedPlan()
    {
        var b = TestPlans.Base(TestPlans.CouncilAnvil());
        var drafts = new FakeDraftStore();
        drafts.Save(TestPlans.Source.DraftKey, new Draft(1, TestPlans.Source.DraftKey, b.Scan.Inventory.InventoryHash,
            TestPlans.Utc(2026, 9, 28, 1, 2), new Tuning(), [new SplitBefore(AnvilFirst)]));
        var offer = DraftOffers.Find(b, drafts, new ScriptedDeriver())!;
        using var h = ReviewHarness.Create([], planBase: b, offer: offer, drafts: drafts);
        await h.SettleAsync();
        await h.Vm.SetIncludedAsync([Council0], false);        // the old session moves past the resumed session's Revision 1
        await h.SettleAsync();

        var next = h.Deriver.Calls.Count;
        h.Deriver.Hold = true;
        var edit = h.Vm.SetIncludedAsync([Council1], false);   // still deriving in the old session when [Resume] is clicked
        await h.Deriver.CallStartedAsync(next);
        await h.Vm.ResumeDraftAsync();
        h.Deriver.ReleaseAll();
        await edit;
        h.Ui.RunAll();

        Assert.Same(offer.Session, h.Vm.Session);
        Assert.Same(h.Vm.Session.Current, h.Vm.Plan);
        Assert.Equal(2, h.Vm.Plan.Groups.Length);
        Assert.Contains(Council1, h.Vm.Plan.Included);
        Assert.Equal(offer.Session.CanUndo, h.Vm.CanUndo);

        await h.Vm.SetIncludedAsync([Council1], false);        // later edits on the resumed session are shown at once
        h.Ui.RunAll();
        Assert.Same(h.Vm.Session.Current, h.Vm.Plan);
        Assert.DoesNotContain(Council1, h.Vm.Plan.Included);
    }
}
