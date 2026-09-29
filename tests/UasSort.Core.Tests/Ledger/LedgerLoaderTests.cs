using System.Text;
using UasSort.Core.Ledger;
using UasSort.Core;
using static UasSort.Core.Tests.Ledger.LedgerLines;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerLoaderTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private const string L = V + @"\.uas-sort";
    private static readonly DateTime T = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private sealed class Opener(Dictionary<string, string> files)
    {
        public List<string> Opened { get; } = [];
        public MemoryStream Open(string path)
        {
            Opened.Add(path);
            return new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(files[path])).ToArray());
        }
    }

    private static FsEntry Fil(string path, uint attrs = 0x20) => new(path, PathRules.FileName(path), false, 100, T, T, T, attrs);

    private static LedgerFolderStatus Status(bool writable, params FsEntry[] top)
        => LedgerFolderStatusBuilder.Build(V, "DESKTOP-A", new LedgerFolderFacts(true,
               new FsEntry(L, ".uas-sort", true, 0, T, T, T, 0x10 | 0x80000), new ListingResult([.. top], []), InSyncRoot: true, writable));

    private static readonly string Dest = V + @"\2026\2026-09\2026-09-27 Zachar Bay\DJI_20260927140127_0123_D.MP4";

    private static Dictionary<string, string> Files() => new(StringComparer.OrdinalIgnoreCase)
    {
        [L + @"\ledger-DESKTOP-A.jsonl"] = Text(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest),
                                                Decision("a91", "DJI_20260725233000_0120_D.DNG", 27_411_200),
                                                FolderRec("fo1", V + @"\2026\2026-09\2026-09-27 Café Bay", "Café Bay",
                                                          new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), "America/Anchorage")),
        [L + @"\ledger-LAPTOP-B.jsonl"] = Text(Revoke("r1", "a91")),
        [L + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl"] = Text(FileRec("f1", "DJI_20260927140127_0123_D.MP4", 105_764_094, Dest)),
        [L + @"\notes.txt"] = "never read",
        [L + @"\sub\ledger-C.jsonl"] = "never read",
    };

    [Fact]
    public void LedgerLoader_ReadsOnlyTopLevelLedgerFiles()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = Status(true,
            Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl"), Fil(L + @"\ledger-DESKTOP-A-LAPTOP-B.jsonl"),
            Fil(L + @"\notes.txt"), Fil(L + @"\sub\ledger-C.jsonl"));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);

        Assert.Equal(3, opener.Opened.Count);
        Assert.DoesNotContain(opener.Opened, p => p.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(opener.Opened, p => p.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.Single(snap.Files);                   // f1 from the own file and its conflict copy, deduped by id
        Assert.Empty(snap.Decisions);                // a91 revoked on LAPTOP-B
        Assert.Equal("Café Bay", Assert.Single(snap.Folders).Value.Description);
        Assert.Equal(3, snap.SourceFiles.Length);
        Assert.Same(status, snap.Status);
        Assert.Empty(snap.ParseIssues);
    }

    [Fact]
    public void LedgerLoader_CloudOnlyLedgerFile_OpensNothing()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = Status(true, Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl", 0x400000 | 0x20));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);
        Assert.Empty(opener.Opened);
        Assert.Equal(LedgerFolderState.CloudOnly, snap.Status.State);
        Assert.Empty(snap.Files);
    }

    [Fact]
    public void LedgerLoader_MissingFolder_OpensNothing()
    {
        var opener = new Opener(Files());
        LedgerFolderStatus status = LedgerFolderStatusBuilder.Build(V, "DESKTOP-A", new LedgerFolderFacts(true, null, null, true, true));
        LedgerSnapshot snap = LedgerLoader.Load(status, opener.Open);
        Assert.Empty(opener.Opened);
        Assert.Equal(LedgerFolderState.Missing, snap.Status.State);
    }

    [Fact]
    public void LedgerLoader_UnwritableFolder_StillReads()
    {
        var opener = new Opener(Files());
        LedgerSnapshot snap = LedgerLoader.Load(Status(false, Fil(L + @"\ledger-DESKTOP-A.jsonl")), opener.Open);
        Assert.Equal(LedgerFolderState.Unwritable, snap.Status.State);
        Assert.Single(snap.Files);
        Assert.Single(snap.Decisions);
    }
}
