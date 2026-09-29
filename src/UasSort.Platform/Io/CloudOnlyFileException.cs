namespace UasSort.Platform.Io;

/// <summary>A cloud-only top-level .uas-sort\ledger*.jsonl: reported as LedgerFolderState.CloudOnly, never a bug (Ref §4.3).</summary>
public sealed class CloudOnlyFileException(string path) : IOException($"{path} is cloud-only; set the .uas-sort folder to Always keep on this device")
{
    public string Path { get; } = path;
}
