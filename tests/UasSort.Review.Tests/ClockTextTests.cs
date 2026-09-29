// tests/UasSort.Review.Tests/ClockTextTests.cs
namespace UasSort.Review.Tests;

public class ClockTextTests
{
    private const string Fix = "To fix the drone clock: RC 2 → Settings → System → Date & time → turn off the network-provided time zone and set the zone for where you are flying. Network time zones can be wrong on ship or hotel Wi-Fi, and the RC keeps the last one it saw until it reconnects.";

    [Fact]
    public void ClockText_MismatchInfoBar_NamesClockZoneAndSites()
    {
        var items = TestPlans.Zachar().Select(TestPlans.ItemOf).ToList();
        var s = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"]);

        var text = ClockText.MismatchInfoBar(s, TestPlans.ZoneClock(), items);

        Assert.Equal("Drone clock is set to UTC−4 (America/New_York), but footage on this card was shot in Alaska (UTC−8). Dates here use local time at each site. " + Fix, text);
    }

    [Fact]
    public void ClockText_MismatchInfoBar_JoinsSeveralSitesAndNearestSampleUsesOffsetOnly()
    {
        var hawaii = new PlanClip("DJI_20260228233000_0009_D.MP4", TestPlans.Utc(2026, 3, 1, 9, 30), "Pacific/Honolulu", 21.47, -158.21,
                              Flags: ItemFlags.ClockMismatch, DroneStamp: new DateTime(2026, 3, 1, 4, 30, 0));
        var items = TestPlans.Zachar().Append(hawaii).Select(TestPlans.ItemOf).ToList();
        var s = TestPlans.Summary(ClockMode.NearestSample, 4, ["America/Anchorage", "Pacific/Honolulu"]);
        var model = new ClockModel(ClockMode.NearestSample, null, [], null, StoredClockMode.Zone, "America/New_York");

        var text = ClockText.MismatchInfoBar(s, model, items);

        Assert.StartsWith("Drone clock is set to UTC−4, but footage on this card was shot in Alaska (UTC−8), Hawaii (UTC−10). ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ClockText_NoMismatch_NoInfoBar()
        => Assert.Null(ClockText.MismatchInfoBar(TestPlans.Summary(), TestPlans.ZoneClock(), []));

    [Fact]
    public void ClockText_TimeTooltip_ShowsUtcDroneClockAndSiteSideBySide()
    {
        var item = TestPlans.ItemOf(TestPlans.Zachar()[1]);
        Assert.Equal("18:06:27 UTC · drone clock 14:06:27 (UTC−4) · 10:06:27 AKDT (UTC−8) · from the video (mvhd) · Drone clock ≠ local time",
                     ClockText.TimeTooltip(item));
        Assert.Equal("10:06 AKDT", ClockText.TimeCell(item));
        Assert.Equal("The drone clock (UTC−4) doesn't match local time here (UTC−8). Dates use local time.", ClockText.ChipTooltip(item));
    }

    [Fact]
    public void ClockText_DroneClockTime_IsMarkedEstimated()
    {
        var item = TestPlans.ItemOf(TestPlans.Zachar()[0] with { Source = TimeSource.DroneClockZone, Flags = ItemFlags.None });
        Assert.Equal("~10:01 AKDT", ClockText.TimeCell(item));
    }

    [Fact]
    public void ClockText_Banner_IsTheCoreHeadline()
    {
        var items = TestPlans.Zachar().Select(TestPlans.ItemOf).ToList();
        const string headline = "Drone clock: US Eastern (America/New_York), learned from 13 videos. Folder dates use local time at each site. It doesn't match local time where this card was shot (Alaska).";
        var s = TestPlans.Summary(ClockMode.Zone, 3, ["America/Anchorage"], headline: headline);

        Assert.Equal(headline, ClockText.Banner(s, TestPlans.ZoneClock(), items));
    }

    [Fact]
    public void ClockText_EveryTimeSourceHasTextAndGlyph()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var src in Enum.GetValues<TimeSource>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ClockText.SourceGlyph(src)), src.ToString());
            Assert.True(texts.Add(ClockText.SourceText(src)), $"duplicate source text for {src}");
        }
    }
}
