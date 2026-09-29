// src/UasSort.Review/Cleanup/CleanupEngine.cs
namespace UasSort.Review;

/// <summary>Ref §10.6 Preparation steps 1–4, done by Part 11's composition root when the page opens (nothing is written).</summary>
public sealed record CleanupPreparation(CleanupInputs? Inputs, string? BlockingText, bool OfferRescan);

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
