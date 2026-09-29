// tests/UasSort.Core.Tests/Cleanup/CleanupTripwireTests.cs
#pragma warning disable CA1861 // expected path lists read best inline next to their scenario; each test runs once
using static UasSort.Core.Tests.Cleanup.CleanupScenario;

namespace UasSort.Core.Tests.Cleanup;

public class CleanupTripwireTests
{
    private static (CleanupScenario S, ItemId Old, ItemId Set) Card()
    {
        var s = new CleanupScenario();
        var old = s.AddVideo(Utc(2026, 7, 20, 20, 0), companions: Comp.Lrf);
        s.AddVideo(Utc(2026, 7, 28, 20, 0));                                 // after the cutoff: not in the plan
        var set = s.AddSet("001_0087", Utc(2026, 7, 21, 20, 0));
        s.AddLoose("MISC/FC9113.db", EntryClass.Skip);
        return (s, old, set);
    }

    private static CardSource Detected => new(CardRoot, Identity, IsBrowsedFolder: false, IsWriteProtected: false);

    private static bool OnCard(FakeFileSystem fs, string cardRelPath) => fs.Exists(PathRules.Join(CardRoot, cardRelPath));

    [Fact]
    public void Eraser_deletes_only_what_the_confirmed_plan_names()
    {
        var (s, old, _) = Card();
        var fs = s.MakeCard();
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        using var eraser = new FakeCardEraserFactory(fs).Open(Detected, Identity, confirmed);

        Assert.True(eraser.DeleteFile(old.CardRelPath) is EraseOk);   // EraseResult is a union: match, don't IsType
        Assert.Empty(fs.CardDeleteViolations);

        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile("DCIM/DJI_001/DJI_20260728120000_0102_D.MP4"));
        Assert.Throws<UnsafeIoException>(() => eraser.DeleteFile("MISC/FC9113.db"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM/DJI_001"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("DCIM"));
        Assert.Throws<UnsafeIoException>(() => eraser.RemoveEmptySetFolder("MISC"));
        Assert.Equal(5, fs.CardDeleteViolations.Count);
        Assert.All(fs.CardDeleteViolations, v => Assert.Equal(IoOp.CardDelete, v.Op));
        Assert.True(OnCard(fs, "DCIM/DJI_001/DJI_20260728120000_0102_D.MP4"));
        Assert.True(OnCard(fs, "MISC/FC9113.db"));
    }

    [Fact]
    public void A_named_set_folder_is_removed_only_once_empty()
    {
        var (s, _, set) = Card();
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        using var eraser = factory.Open(Detected, Identity, s.Confirmed(Before(2026, 7, 26)));
        Assert.True(eraser.RemoveEmptySetFolder(set.CardRelPath) is EraseError { Win32Error: 145 });
        foreach (var m in new[] { "PANO_0001.DNG", "PANO_0002.DNG", "PANO_0003.DNG" })
            Assert.True(eraser.DeleteFile($"{set.CardRelPath}/{m}") is EraseOk);
        Assert.True(eraser.RemoveEmptySetFolder(set.CardRelPath) is EraseOk);
        Assert.Equal(new[] { "DCIM/PANORAMA/001_0087/" }, factory.AllDeleted.Where(p => p.EndsWith('/')));
        Assert.False(OnCard(fs, set.CardRelPath));
        Assert.Empty(fs.CardDeleteViolations);
    }

    [Fact]
    public void Any_context_without_a_verified_volume_and_a_plan_trips_the_wire()
    {
        var (s, old, _) = Card();
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        var confirmed = s.Confirmed(Before(2026, 7, 26));

        using (var noPlan = factory.OpenUnchecked(FakeLayout.Context() with { CardIsVerifiedCardVolume = true }))
            Assert.Throws<UnsafeIoException>(() => noPlan.DeleteFile(old.CardRelPath));
        using (var unverified = factory.OpenUnchecked(FakeLayout.Context() with { Cleanup = confirmed }))
            Assert.Throws<UnsafeIoException>(() => unverified.DeleteFile(old.CardRelPath));
        Assert.Equal(2, fs.CardDeleteViolations.Count);
        Assert.True(OnCard(fs, old.CardRelPath));
        Assert.Empty(factory.AllDeleted);
    }

    [Fact]
    public void The_factory_refuses_browsed_write_protected_unverified_removed_and_foreign_cards()
    {
        var (s, _, _) = Card();
        var confirmed = s.Confirmed(Before(2026, 7, 26));
        var fs = s.MakeCard();
        var factory = new FakeCardEraserFactory(fs);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { IsBrowsedFolder = true }, Identity, confirmed));
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { IsWriteProtected = true }, Identity, confirmed));
        factory.VolumeVerified = false;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        factory.VolumeVerified = true;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity with { VolumeSerial = 0xDEADBEEF }, confirmed));
        fs.SetCardIdentity(CardRoot, Identity with { VolumeSerial = 0x5E6F7A8B });          // another card in the slot
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        fs.SetCardIdentity(CardRoot, Identity);
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected with { Root = @"F:\" }, Identity, confirmed));   // plan is for E:\
        fs.Faults.CardRemoved = true;
        Assert.Throws<UnsafeIoException>(() => factory.Open(Detected, Identity, confirmed));
        Assert.Equal(0, factory.OpenCount);
        Assert.Empty(factory.AllDeleted);
        Assert.Empty(fs.CardDeleteViolations);
    }
}
