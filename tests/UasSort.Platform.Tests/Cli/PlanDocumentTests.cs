using System.Text.Json;
using UasSort.Cli;
#pragma warning disable CA1861 // expected name lists read best inline next to their assertion; each test runs once

namespace UasSort.Platform.Tests.Cli;

public sealed class PlanDocumentTests
{
    private static JsonElement Serialize() =>
        JsonElement.Parse(JsonSerializer.Serialize(CliSamples.Document(), CliJsonContext.Default.PlanDocument));

    [Fact]
    public void TopLevel_MatchesTheRefSchema()
    {
        var root = Serialize();
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        string[] names = root.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "v", "card", "settings", "clock", "watermarkUtc", "groups", "photoDays", "sets", "other", "issues" }, names);
        Assert.Equal("2026-09-27T18:24:16Z", root.GetProperty("watermarkUtc").GetString());
    }

    [Fact]
    public void Card_And_Clock_UseCamelCaseAndEnumNames()
    {
        var root = Serialize();
        var identity = root.GetProperty("card").GetProperty("identity");
        Assert.Equal("1A2B3C4D", identity.GetProperty("serial").GetString());
        Assert.Equal(JsonValueKind.Null, identity.GetProperty("label").ValueKind);
        Assert.Equal(256060514304, identity.GetProperty("totalBytes").GetInt64());
        Assert.Equal("FC9113", root.GetProperty("card").GetProperty("model").GetString());
        var clock = root.GetProperty("clock");
        Assert.Equal("Zone", clock.GetProperty("mode").GetString());
        Assert.Equal(25, clock.GetProperty("mismatch").GetProperty("items").GetInt32());
        Assert.Equal("America/Anchorage", clock.GetProperty("mismatch").GetProperty("siteZones")[0].GetString());
        Assert.Equal(50, root.GetProperty("settings").GetProperty("radiusMiles").GetDouble());
    }

    [Fact]
    public void Group_MatchesTheRefExample()
    {
        var g = Serialize().GetProperty("groups")[0];
        Assert.Equal(CliSamples.CouncilId, g.GetProperty("id").GetString());
        Assert.Equal("Append", g.GetProperty("target").GetString());
        Assert.Equal(@"2026\2026-07\2026-07-25 Council Road", g.GetProperty("relPath").GetString());
        Assert.Equal("Medium", g.GetProperty("confidence").GetString());
        Assert.Equal("2026-07-25", g.GetProperty("start").GetString());
        var b = g.GetProperty("boundaryBefore");
        Assert.Equal("DayGap", b.GetProperty("cause").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("jumpMiles").ValueKind);
        Assert.Equal(62, b.GetProperty("dayGap").GetInt32());
        var v = g.GetProperty("videos")[0];
        Assert.Equal("Imported", v.GetProperty("status").GetString());
        Assert.False(v.GetProperty("included").GetBoolean());
        Assert.Equal("2026-07-26T03:26:55Z", v.GetProperty("captureUtc").GetString());
        Assert.Equal("Mvhd", v.GetProperty("timeSource").GetString());
        Assert.Equal("ClockMismatch", v.GetProperty("flags")[0].GetString());
        Assert.True(g.GetProperty("daySplits")[0].GetProperty("emphasised").GetBoolean());
        Assert.Equal(new[] { "MediumAppend", "EmphasisedDaySplit" }, g.GetProperty("issues").EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    [Fact]
    public void PhotoDays_Sets_Other_Issues_UseTheRefNames()
    {
        var root = Serialize();
        var day = root.GetProperty("photoDays")[0];
        Assert.Equal(8, day.GetProperty("new").GetInt32());
        Assert.Equal(4, day.GetProperty("probablyImported").GetInt32());
        Assert.Equal("America/Anchorage", day.GetProperty("tz").GetString());
        Assert.Equal("DateSuffixed", root.GetProperty("sets")[0].GetProperty("resolution").GetString());
        Assert.Equal("Unknown", root.GetProperty("other")[0].GetProperty("class").GetString());
        var issue = root.GetProperty("issues")[0];
        Assert.Equal("Blocking", issue.GetProperty("severity").GetString());
        Assert.Equal("EmptyFolderName", issue.GetProperty("code").GetString());
        Assert.False(issue.GetProperty("requiresAck").GetBoolean());
    }

    [Fact]
    public void Text_NewFolderLine_IsTheRefFormat()
    {
        var w = new StringWriter();
        PlanTextRenderer.Render(CliSamples.Document(), w);
        string text = w.ToString();
        Assert.Contains("NEW FOLDER 2026\\2026-09\\2026-09-27 · 13 clips · Sep 27 · issues: EmptyFolderName", text, StringComparison.Ordinal);
        Assert.Contains("APPEND (Medium) 2026\\2026-07\\2026-07-25 Council Road · 2 clips · Jul 25–26 · issues: MediumAppend, EmphasisedDaySplit",
                        text, StringComparison.Ordinal);
        Assert.Contains("different day, 34 mi from Council Road", text, StringComparison.Ordinal);
        Assert.Contains("PHOTO DAYS", text, StringComparison.Ordinal);
        Assert.Contains("001_0087 2026-09-27", text, StringComparison.Ordinal);
        Assert.Contains("DCIM/DJI_A001/x.MP4", text, StringComparison.Ordinal);
        Assert.Contains("Blocking EmptyFolderName", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 9, 27, 2026, 9, 27, "Sep 27")]
    [InlineData(2026, 7, 25, 2026, 7, 26, "Jul 25–26")]
    [InlineData(2026, 7, 31, 2026, 8, 2, "Jul 31–Aug 2")]
    [InlineData(2025, 12, 31, 2026, 1, 2, "Dec 31, 2025–Jan 2, 2026")]
    public void Text_DayRanges(int y1, int m1, int d1, int y2, int m2, int d2, string expected) =>
        Assert.Equal(expected, PlanTextRenderer.FormatDays(new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2)));
}
