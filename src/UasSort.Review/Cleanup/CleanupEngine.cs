// src/UasSort.Review/Cleanup/CleanupEngine.cs
namespace UasSort.Review;

/// <summary>Ref §10.6 Preparation steps 1–4, done by Part 11's composition root when the page opens (nothing is written).
/// KeepOnDevice: the [Keep on this device] action of a cloud-only ledger folder refusal (Ref §7.5, §12); the page then prepares again.</summary>
public sealed record CleanupPreparation(CleanupInputs? Inputs, string? BlockingText, bool OfferRescan)
{
    public Action? KeepOnDevice { get; init; }
}

/// <summary>Ref §7.5 precondition and the §12 row "a ledger file is cloud-only, or the folder is unwritable → Blocking for Commit and
/// Card cleanup, with [Keep on this device]": before Card cleanup plans anything, ILedgerStore.Check() (attributes only) must pass;
/// then the ledger is loaded. Every refusal is a Blocking preparation (same texts as Preflight), never an exception out of Open().</summary>
public static class CleanupLedgerGate
{
    public static (LedgerSnapshot? Ledger, CleanupPreparation? Refused) Load(ILedgerStore store, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        var folder = LedgerPaths.For(settings.VideoRoot);
        try
        {
            var status = store.Check();
            var refusal = status.State switch
            {
                LedgerFolderState.CloudOnly =>
                    $"Set {Path.GetFileName(settings.VideoRoot.TrimEnd('\\'))}\\{LedgerPaths.FolderName} to Always keep on this device",
                LedgerFolderState.Unwritable => $"Can't write the history file in {folder}",
                LedgerFolderState.Unlistable => $"Can't list {folder}",
                LedgerFolderState.VideoRootMissing => $"{settings.VideoRoot} is not available",
                _ => null,
            };
            if (refusal is not null)
                return (null, new CleanupPreparation(null, refusal, true)
                {
                    KeepOnDevice = status.State == LedgerFolderState.CloudOnly ? store.KeepOnDevice : null,
                });
            return (store.Load(), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnsafeIoException)
        {
            return (null, new CleanupPreparation(null, $"The history file can't be read: {e.Message}", true));
        }
    }
}

/// <summary>What the cleanup page calls outside Review. <c>Run</c> is bound (Part 11) to exactly
/// <c>(confirmed, progress, ct) => CleanupExecutor.RunAsync(confirmed, cleanupEnvironment, progress, ct)</c>: Part 08 owns the whole run.</summary>
public sealed record CleanupEngine(
    Func<CleanupPreparation> Prepare,
    Func<ConfirmedCleanupPlan, IProgress<CleanupProgress>, CancellationToken, Task<CleanupResult>> Run,
    Func<Task<VerdictLevel>> Rescan,
    Func<CleanupReport, string> SaveReport,
    IDeviceEject Eject);

public enum CleanupStep { Choose, Review, Confirm, Deleting, Result }

public sealed class KeptGroupVm(string reason, IReadOnlyList<string> lines)
{
    public string Reason { get; } = reason;
    public IReadOnlyList<string> Lines { get; } = lines;
    public override string ToString() => Reason;
}
