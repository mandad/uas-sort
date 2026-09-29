// tests/UasSort.Core.Tests/Ledger/TestLedger.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

internal static class TestLedger
{
    public static readonly string OwnFile = LedgerLines.Folder + @"\ledger-DESKTOP-A.jsonl";

    public static LedgerFolderStatus Status(params string[] files) => LedgerSnapshots.Detached(LedgerLines.Folder, files);

    public static LedgerSnapshot Snapshot(params LedgerRecord[] records)
        => LedgerReader.Read([new LedgerFileText(OwnFile, LedgerLines.Text(records))], Status(OwnFile));

    public static LedgerSnapshot Empty() => LedgerSnapshots.Empty(Status());
}
