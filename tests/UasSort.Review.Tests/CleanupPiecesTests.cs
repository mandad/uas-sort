// tests/UasSort.Review.Tests/CleanupPiecesTests.cs
namespace UasSort.Review.Tests;

public class CleanupPiecesTests
{
    [Theory]
    [InlineData(true, false, false, false, true, null, false, "Wait until the offload finishes")]
    [InlineData(false, true, false, false, true, null, false, "Wait until the scan finishes")]
    [InlineData(false, false, true, false, true, null, false, "The card is write-protected (lock switch)")]
    [InlineData(false, false, false, true, true, null, false, "Cleanup works only on a detected card. A browsed folder could be a backup copy.")]
    [InlineData(false, false, false, false, true, "not removable media", false, "This doesn't look like a drone card (it may be a backup drive)")]
    [InlineData(false, false, false, false, false, null, false, "Rescan first")]
    [InlineData(false, false, false, false, true, null, true, null)]
    public void CleanupAvailability_FollowsTheTable(bool commit, bool scan, bool writeProtected, bool browsed, bool present, string? refusal,
                                                    bool enabled, string? tooltip)
    {
        var source = new CardSource(@"E:\", TestPlans.Card, browsed, writeProtected);
        Assert.Equal((enabled, tooltip), CleanupAvailability.For(new CleanupContext(commit, scan, source, present, refusal)));
    }

    // Task U4: the visible reason; for a volume that fails the check it names the failing rule and its facts.
    [Theory]
    [InlineData(false, false, "This doesn't look like a drone card (it may be a backup drive)", "rule 4: bus Usb, removable media false",
                "This doesn't look like a drone card (rule 4: bus Usb, removable media false)")]
    [InlineData(false, false, "This doesn't look like a drone card (it may be a backup drive)", null,
                "This doesn't look like a drone card (it may be a backup drive)")]
    [InlineData(true, false, null, null, "The card is write-protected (lock switch)")]
    [InlineData(false, true, null, null, null)]
    public void CleanupAvailability_Text_IsTheVisibleReason(bool writeProtected, bool enabled, string? refusal, string? detail, string? text)
    {
        var context = new CleanupContext(false, false, new CardSource(@"E:\", TestPlans.Card, false, writeProtected), true, refusal, detail);
        Assert.Equal(enabled, CleanupAvailability.For(context).Enabled);
        Assert.Equal(text, CleanupAvailability.Text(context));
    }

    [Fact]
    public void CleanupAvailability_NoCardScanned_RescanFirst()
        => Assert.Equal((false, "Rescan first"), CleanupAvailability.For(new CleanupContext(false, false, null, false, null)));

    [Fact]
    public void CleanupRow_TextFieldsAndToggles()
    {
        var c = CleanupFixture.Candidate("DCIM/DJI_001/DJI_20260726202000_0014_D.MP4", ItemKind.Video, 7_000_000, null,
                                         new DateTime(2026, 7, 26, 20, 20, 0), TestPlans.Utc(2026, 7, 27, 4, 20), NotInLibraryReason.Unfinished,
                                         "unfinished recording; the drone may still repair it", place: "near Anvil Mountain · 0.2 mi", ticked: true);
        var set = new List<RowDecision>();
        var row = new CleanupRowVm(c, (_, d) => set.Add(d));
        row.Update(c, RowDecision.Keep, undecidedNewRow: false);

        Assert.Equal("Jul 26 20:20 AKDT", row.DateText);
        Assert.Equal("unfinished · ~7 MB", row.LengthText);
        Assert.Equal("near Anvil Mountain · 0.2 mi", row.LocationText);
        Assert.Equal("ticked for offload", row.Badge);
        Assert.Equal("Jul 26 20:20 AKDT · unfinished · ~7 MB · near Anvil Mountain · 0.2 mi · 7 MB · unfinished recording; the drone may still repair it · ticked for offload", row.ToString());
        row.DeleteCommand.Execute(null);
        Assert.Equal<RowDecision>([RowDecision.Delete], set);

        var noGps = CleanupFixture.Candidate("DCIM/DJI_001/DJI_20260720190000_0050_D.MP4", ItemKind.Video, 1_200_000_000, TimeSpan.FromSeconds(222),
                                             new DateTime(2026, 7, 20, 19, 0, 0), TestPlans.Utc(2026, 7, 21, 3, 0), NotInLibraryReason.New, "new: not in your library",
                                             location: new GeoPoint(64.5627, -165.3709));
        var r2 = new CleanupRowVm(noGps, (_, _) => { });
        r2.Update(noGps, RowDecision.Undecided, undecidedNewRow: true);
        Assert.Equal("3:42", r2.LengthText);
        Assert.Equal("64.5627, -165.3709", r2.LocationText);
        Assert.Equal("new in range", r2.Badge);
    }

    [Fact]
    public void CleanupResult_TotalsProblemsAndStillListed()
    {
        var u1 = new ItemId("DCIM/DJI_001/a.MP4");
        var u2 = new ItemId("DCIM/DJI_001/b.MP4");
        var u3 = new ItemId("DCIM/PANORAMA/001_0087");
        var result = CleanupFixture.Result(
            [new Deleted(u1, 2, 1_300_000_000, false), new SkippedChanged(u2, u2.CardRelPath, 5, TestPlans.Utc(2026, 9, 28, 1, 0)),
             new PartiallyDeleted(u3, ["DCIM/PANORAMA/001_0087/PANO_0001.JPG"], ["DCIM/PANORAMA/001_0087/PANO_0002.JPG"], "access denied")],
            stop: null, freeAfter: 73_800_000_000, stillListed: ["DCIM/DJI_001/a.LRF"]);
        var vm = new CleanupResultVm(result, VerdictLevel.Safe, @"C:\r.json", @"E:\", new FakeEjectOk());

        Assert.Equal("Deleted 3 files (1.3 GB) · E: now has 73.8 GB free", vm.HeadlineText);
        Assert.Equal(2, vm.Problems.Count);
        Assert.Contains(vm.Problems, p => p.StartsWith("b.MP4: skipped, it changed since the scan", StringComparison.Ordinal));
        Assert.Contains(vm.Problems, p => p.Contains("still on the card: PANO_0002.JPG", StringComparison.Ordinal));
        Assert.Equal("still on the card: DCIM/DJI_001/a.LRF (another program held it open)", Assert.Single(vm.StillListed));
        Assert.Equal("Safe to format", vm.VerdictText);
        Assert.StartsWith("Safely remove the card before putting it back in the drone.", vm.SafeRemovalText, StringComparison.Ordinal);
        Assert.Equal("Eject E:", vm.Eject.Text);
    }

    [Fact]
    public void CleanupResult_FolderKept_NotedForPanoramaAndHyperlapseSetsOnly()
    {
        var result = CleanupFixture.Result(
            [new Deleted(new ItemId("DCIM/PANORAMA/001_0087"), 3, 30_000_000, false),
             new Deleted(new ItemId("DCIM/HYPERLAPSE/HYPERLAPSE_0003"), 240, 900_000_000, false),
             new Deleted(new ItemId("DCIM/HYPERLAPSE/HYPERLAPSE_0004"), 240, 900_000_000, true),
             new Deleted(new ItemId("DCIM/DJI_001/a.MP4"), 2, 1_300_000_000, false)],
            stop: null, freeAfter: 73_800_000_000, stillListed: []);
        var vm = new CleanupResultVm(result, VerdictLevel.Safe, @"C:\r.json", @"E:\", new FakeEjectOk());

        Assert.Equal(["001_0087: folder kept", "HYPERLAPSE_0003: folder kept"], vm.Problems);
    }

    [Fact]
    public void CleanupResult_ClosingReadError_ReplacesTheFreeSpaceLineAndStillListed()
    {
        var u1 = new ItemId("DCIM/DJI_001/a.MP4");
        const string error = "Couldn't re-read E: after cleanup: the card couldn't be listed (Win32 error 21)";
        var result = CleanupFixture.Result([new Deleted(u1, 2, 1_300_000_000, false)], stop: null, freeAfter: 73_800_000_000,
                                           stillListed: ["DCIM/DJI_001/a.LRF"], closingReadError: error);
        var vm = new CleanupResultVm(result, VerdictLevel.Safe, @"C:\r.json", @"E:\", new FakeEjectOk());

        Assert.Equal($"Deleted 2 files (1.3 GB) · {error}", vm.HeadlineText);
        Assert.DoesNotContain("now has", vm.HeadlineText, StringComparison.Ordinal);
        Assert.Empty(vm.StillListed);
    }
}

internal sealed class FakeEjectOk : IDeviceEject
{
    public List<string> Ejected { get; } = [];
    public EjectResult Eject(string volumeRoot) { Ejected.Add(volumeRoot); return new Ejected(volumeRoot); }
}
