using System.Text;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Reads the listed local ledger files through the caller's guarded opener (Ref §4.1 ILedgerStore.Load, §4.3 exemption 1).</summary>
public static class LedgerLoader
{
    public static LedgerSnapshot Load(LedgerFolderStatus status, Func<string, Stream> openRead)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(openRead);
        if (status.State is LedgerFolderState.VideoRootMissing or LedgerFolderState.Missing or LedgerFolderState.CloudOnly
            || status.LedgerFiles.IsEmpty)
        {
            return LedgerSnapshots.Empty(status);
        }

        var sources = new List<LedgerFileText>(status.LedgerFiles.Length);
        foreach (string path in status.LedgerFiles)
        {
            using Stream stream = openRead(path);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            sources.Add(new LedgerFileText(path, reader.ReadToEnd()));
        }
        return LedgerReader.Read(sources, status);
    }
}
