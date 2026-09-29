// tests/UasSort.Core.Tests/Ledger/LedgerFolderStatusTests.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

public sealed class LedgerFolderStatusTests
{
    private const string V = @"C:\Lib\UAS Videos";
    private const string L = V + @"\.uas-sort";
    private const string Machine = "DESKTOP-A";
    private static readonly DateTime T = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private static FsEntry Dir(string path, uint attrs = 0x10) => new(path, PathRules.FileName(path), true, 0, T, T, T, attrs);
    private static FsEntry Fil(string path, uint attrs = 0x20) => new(path, PathRules.FileName(path), false, 100, T, T, T, attrs);
    private static ListingResult Listing(params FsEntry[] entries) => new([.. entries], []);

    private static LedgerFolderStatus Build(FsEntry? folder, ListingResult? top, bool inSync = true, bool writable = true, bool rootExists = true)
        => LedgerFolderStatusBuilder.Build(V, Machine, new LedgerFolderFacts(rootExists, folder, top, inSync, writable));

    private static readonly FsEntry PinnedFolder = Dir(L, 0x10 | 0x80000);

    [Fact]
    public void LedgerStatus_DerivedLocation()
    {
        Assert.Equal(@"D:\Vids\.uas-sort", LedgerPaths.For(@"D:\Vids"));
        Assert.Equal(@"D:\Vids\.uas-sort\ledger-PC1.jsonl", LedgerPaths.OwnFile(@"D:\Vids", "PC1"));
        Assert.Equal(L, Build(null, null).Folder);
    }

    [Fact]
    public void LedgerStatus_MissingFolder_IsMissing()
    {
        LedgerFolderStatus s = Build(null, null);
        Assert.Equal(LedgerFolderState.Missing, s.State);
        Assert.False(s.Exists);
        Assert.Empty(s.LedgerFiles);
    }

    [Fact]
    public void LedgerStatus_MissingVideoRoot_IsVideoRootMissing()
        => Assert.Equal(LedgerFolderState.VideoRootMissing, Build(null, null, rootExists: false).State);

    [Fact]
    public void LedgerStatus_FolderWithoutLedgerFiles_IsEmpty()
        => Assert.Equal(LedgerFolderState.Empty, Build(PinnedFolder, Listing(Fil(L + @"\notes.txt"), Fil(L + @"\ledgers.txt"))).State);

    [Fact]
    public void LedgerStatus_OnlyTopLevelLedgerJsonlCount()
    {
        LedgerFolderStatus s = Build(PinnedFolder, Listing(
            Fil(L + @"\ledger-DESKTOP-A.jsonl"),
            Fil(L + @"\ledger-LAPTOP-B.jsonl"),
            Fil(L + @"\ledger-LAPTOP-B-DESKTOP-A.jsonl"),   // OneDrive conflict copy
            Fil(@"C:\LIB\UAS VIDEOS\.UAS-SORT\LEDGER-C.JSONL"),
            Fil(L + @"\notes.txt"),
            Dir(L + @"\sub"),
            Fil(L + @"\sub\ledger-D.jsonl"),
            Fil(V + @"\.uas-sort2\ledger-E.jsonl"),
            Fil(V + @"\ledger-X.jsonl")));
        Assert.Equal(LedgerFolderState.Ok, s.State);
        Assert.True(s.Pinned);
        Assert.Equal(4, s.LedgerFiles.Length);
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains("notes", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains(@"\sub\", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.Contains(".uas-sort2", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(s.LedgerFiles, f => f.EndsWith(@"UAS Videos\ledger-X.jsonl", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, s.OtherMachineFiles.Length);
        Assert.DoesNotContain(L + @"\ledger-DESKTOP-A.jsonl", s.OtherMachineFiles);
    }

    [Fact]
    public void LedgerStatus_UnpinnedInSyncRoot_IsNotPinned_OutsideSyncRootIsOk()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"));
        Assert.Equal(LedgerFolderState.NotPinned, Build(Dir(L), top).State);
        Assert.Equal(LedgerFolderState.Ok, Build(Dir(L), top, inSync: false).State);
    }

    [Fact]
    public void LedgerStatus_CloudOnlyLedgerFile_IsCloudOnlyAndBeatsUnwritable()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"), Fil(L + @"\ledger-LAPTOP-B.jsonl", 0x400000 | 0x20));
        LedgerFolderStatus s = Build(PinnedFolder, top, writable: false);
        Assert.Equal(LedgerFolderState.CloudOnly, s.State);
        Assert.Equal(new[] { L + @"\ledger-LAPTOP-B.jsonl" }, s.CloudOnlyFiles);
        Assert.Equal(LedgerFolderState.CloudOnly, Build(PinnedFolder, Listing(Fil(L + @"\ledger-B.jsonl", 0x1000))).State);
        Assert.Equal(LedgerFolderState.CloudOnly, Build(PinnedFolder, Listing(Fil(L + @"\ledger-B.jsonl", 0x40000))).State);
    }

    [Fact]
    public void LedgerStatus_Unwritable_BeatsNotPinnedAndMissing()
    {
        ListingResult top = Listing(Fil(L + @"\ledger-DESKTOP-A.jsonl"));
        Assert.Equal(LedgerFolderState.Unwritable, Build(Dir(L), top, writable: false).State);
        Assert.Equal(LedgerFolderState.Unwritable, Build(null, null, writable: false).State);
    }
}
