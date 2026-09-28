// tests/UasSort.Core.Tests/Ledger/LedgerParserTests.cs
using UasSort.Core.Ledger;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerParserTests
{
    private const string Dest = @"C:\Lib\UAS Videos\2026\2026-09\2026-09-27 Zachar Bay\";
    private static readonly string OwnA = Folder + @"\ledger-DESKTOP-A.jsonl";
    private static readonly string CopyA = Folder + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl";
    private static readonly string OwnB = Folder + @"\ledger-LAPTOP-B.jsonl";

    private static readonly string F1 = Line(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest + "DJI_20260927140127_0123_D.MP4"));
    private static readonly string F2 = Line(FileRec("f2", "DJI_20260927140144_0124_D.MP4", 98_000_000, Dest + "DJI_20260927140144_0124_D.MP4"));
    private static readonly string F3 = Line(FileRec("f3", "DJI_20260927142416_0148_D.MP4", 77_000_000, Dest + "DJI_20260927142416_0148_D.MP4"));
    private const string Half = """{"t":"file","v":1,"id":"f9","machine":"DESKTOP-A","run":"8f1c""";

    private static string[] Ids(LedgerParseResult r) => [.. r.Records.Select(p => p.Record.Id)];

    [Fact]
    public void LedgerParser_UnionOfMachinesAndConflictCopies_DedupesById()
    {
        var r = LedgerParser.Parse([
            new LedgerFileText(OwnA, F1 + "\n" + F2 + "\n"),
            new LedgerFileText(CopyA, F1 + "\n" + F2 + "\n"),
            new LedgerFileText(OwnB, Line(Decision("d1", "DJI_20260725233000_0116_D.DNG", 27_411_200, machine: "LAPTOP-B")) + "\n")]);
        Assert.Equal(["d1", "f1", "f2"], Ids(r).Order(StringComparer.Ordinal));
        Assert.Empty(r.Issues);
        Assert.Equal([CopyA, OwnA, OwnB], r.SourceFiles);
    }

    [Fact]
    public void LedgerParser_TornFinalLine_IsSkippedWithoutAnIssue()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + Half)]);
        Assert.Equal(["f1"], Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_UnterminatedFinalLine_IsSkippedEvenWhenComplete()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + F2)]);
        Assert.Equal(["f1"], Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_BadMiddleLine_IsAParseIssueWithFileAndLine()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\n" + "garbage\n" + F2 + "\n")]);
        Assert.Equal(["f1", "f2"], Ids(r));
        var issue = Assert.Single(r.Issues);
        Assert.Equal(OwnA, issue.File);
        Assert.Equal(2, issue.Line);
        Assert.StartsWith("invalid record", issue.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DESKTOP-A")]
    [InlineData("LAPTOP-B")]
    public void LedgerParser_CrashMidAppendThenRepair_NoIssueAndLaterRecordsParse(string machine)
    {
        // own file after a crash (line 2 half written), then OpenOwn() appended "\n" + torn(2), then a normal run appended f3
        string path = Folder + $@"\ledger-{machine}.jsonl";
        string text = F1 + "\n" + Half + "\n" + Line(Torn("t1", 2, machine)) + "\n" + F3 + "\n";
        var r = LedgerParser.Parse([new LedgerFileText(path, text)]);
        Assert.Empty(r.Issues);
        Assert.Equal(["f1", "t1", "f3"], Ids(r));
    }

    [Fact]
    public void LedgerParser_TornRecordCannotNameALaterLine()
    {
        string text = F1 + "\n" + Line(Torn("t1", 3)) + "\n" + "garbage\n" + F2 + "\n";
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, text)]);
        Assert.Equal(3, Assert.Single(r.Issues).Line);
    }

    [Fact]
    public void LedgerParser_TornRecordOnlyAppliesToItsOwnFile()
    {
        var r = LedgerParser.Parse([
            new LedgerFileText(OwnA, F1 + "\n" + Line(Torn("t1", 1)) + "\n"),
            new LedgerFileText(OwnB, "garbage\n" + F2 + "\n")]);
        var issue = Assert.Single(r.Issues);
        Assert.Equal((OwnB, 1), (issue.File, issue.Line));
    }

    [Fact]
    public void LedgerParser_BlankLinesAndCrLf_AreIgnored()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, F1 + "\r\n\r\n" + F2 + "\r\n")]);
        Assert.Equal(["f1", "f2"], Ids(r));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void LedgerParser_EmptyFile_GivesNothing()
    {
        var r = LedgerParser.Parse([new LedgerFileText(OwnA, "")]);
        Assert.Empty(r.Records);
        Assert.Empty(r.Issues);
        Assert.Equal([OwnA], r.SourceFiles);
    }
}
