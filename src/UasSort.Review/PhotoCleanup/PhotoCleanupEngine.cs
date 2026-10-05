// src/UasSort.Review/PhotoCleanup/PhotoCleanupEngine.cs
namespace UasSort.Review;

/// <summary>What the page opens with: the survey, or why the page is blocked (with [Keep on this device] for a cloud-only ledger
/// folder), and why verify mode is unavailable.</summary>
public sealed record PhotoCleanupPreparation(PhotoSurvey? Survey, string? BlockingText, string? VerifyUnavailableText, string? LightroomFolder)
{
    public Action? KeepOnDevice { get; init; }
}

/// <summary>What PhotoCleanupVm calls outside Review. Created by <see cref="PhotoCleanupEngines.Create"/> (Part P.14 composes it).</summary>
public sealed record PhotoCleanupEngine(
    Func<IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPreparation>> Prepare,
    Func<PhotoSurvey, PhotoCleanupRequest, IProgress<PhotoScanProgress>, CancellationToken, Task<PhotoCleanupPlan>> Plan,
    Func<ConfirmedPhotoCleanupPlan, IProgress<PhotoCleanupProgress>, CancellationToken, Task<PhotoCleanupResult>> Run,
    Func<PhotoCleanupReport, string> SaveReport,
    IShellLauncher Shell);

/// <summary>PhotoRoot and Settings.LightroomFolder are canonical (IPathFacts.Canonical, Task P.14), so the plan's paths match the recycler's
/// guard context and the Lightroom listing, its reads and the reader's guard all use the same form of the folder. Clock dates Picture
/// Offload files without a ledger record as the scan would (resolved ambiguity 8).</summary>
public sealed record PhotoCleanupPorts(Settings Settings, string PhotoRoot, IDirectoryLister Lister, IPhotoFileReader Reader, ILedgerStore Ledger,
                                       PhotoCaptureClock Clock, PhotoCleanupEnvironment Environment, Func<string, bool> FolderExists,
                                       IReportStore Reports, IShellLauncher Shell)
{
    public PhotoRootThumbnails? Thumbnails { get; init; }
}

public static class PhotoCleanupEngines
{
    public static PhotoCleanupEngine Create(PhotoCleanupPorts p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var exif = new PhotoExifCache(p.Reader);
        return new PhotoCleanupEngine(
            (progress, ct) => Task.Run(() => Prepare(p, exif, progress, ct), ct),
            (survey, request, progress, ct) => Task.Run(() => Plan(p, exif, survey, request, progress, ct), ct),
            (confirmed, progress, ct) => PhotoCleanupExecutor.RunAsync(confirmed, p.Environment, progress, ct),
            p.Reports.Save,
            p.Shell);
    }

    /// <summary>Ledger gate (Ref §7.5: a cloud-only or unwritable history blocks up front), the photo root's availability and full listing
    /// (an incomplete listing blocks), then the survey; and whether verify mode is available.</summary>
    internal static PhotoCleanupPreparation Prepare(PhotoCleanupPorts p, PhotoExifCache exif, IProgress<PhotoScanProgress> progress, CancellationToken ct)
    {
        var (ledger, refused) = CleanupLedgerGate.Load(p.Ledger, p.Settings);
        if (refused is not null)
            return new PhotoCleanupPreparation(null, refused.BlockingText, null, null) { KeepOnDevice = refused.KeepOnDevice };
        if (!p.FolderExists(p.PhotoRoot)) return new PhotoCleanupPreparation(null, $"The photo folder {p.PhotoRoot} is not available", null, null);
        var listing = p.Lister.Enumerate(p.PhotoRoot, recurse: true, PhotoCleanupPlanner.Excludes);
        if (!listing.Errors.IsEmpty)
        {
            var (path, code) = listing.Errors[0];
            return new PhotoCleanupPreparation(null, string.Create(CultureInfo.InvariantCulture,
                $"Part of {p.PhotoRoot} couldn't be listed ({path}: Win32 error {code}); nothing will be moved"), null, null);
        }
        var survey = PhotoCleanupPlanner.Survey(p.PhotoRoot, listing, ledger!, exif, p.Clock, progress, ct);
        p.Thumbnails?.Use(p.PhotoRoot, survey.Items);
        var lightroom = p.Settings.LightroomFolder;
        var verify = lightroom is null ? "Set a Lightroom library folder in Settings to verify against Lightroom"
                   : !p.FolderExists(lightroom) ? $"The Lightroom folder {lightroom} is not available"
                   : null;
        return new PhotoCleanupPreparation(survey, null, verify, verify is null ? lightroom : null);
    }

    internal static PhotoCleanupPlan Plan(PhotoCleanupPorts p, PhotoExifCache exif, PhotoSurvey survey, PhotoCleanupRequest request,
                                          IProgress<PhotoScanProgress> progress, CancellationToken ct)
    {
        LightroomIndex? index = null;
        if (request.Mode == PhotoCleanupMode.Verify)
        {
            var folder = request.LightroomFolder ?? throw new InvalidOperationException("Verify mode needs the Lightroom folder");
            index = PhotoCleanupPlanner.VerifyRange(survey, request.Cutoff) is { } range
                ? LightroomIndex.Build(folder, p.Lister, exif, range.From, range.To, progress, ct)
                : LightroomIndex.Empty(folder);
        }
        return PhotoCleanupPlanner.Build(survey, request, index, exif, progress, ct);
    }
}
