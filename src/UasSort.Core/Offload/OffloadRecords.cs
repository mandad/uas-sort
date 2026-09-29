// src/UasSort.Core/Offload/OffloadRecords.cs
using System.Globalization;

namespace UasSort.Core.Offload;

/// <summary>Ledger records written during Commit (Ref §10.4, fields as Ref §11).</summary>
public static class OffloadRecords
{
    public const int Version = 1;

    public static string NewId() => Guid.NewGuid().ToString();

    public static FileRecord File(CopyOutcome outcome, string runId, string machine, DateTime atUtc, JobMeta? meta)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var job = outcome.Job;
        var (xxh, verify) = outcome switch
        {
            Verified v => (v.Hash.ToString("x32", CultureInfo.InvariantCulture), v.Mode == VerifyMode.Cached ? "cached" : "unbuffered"),
            AlreadyThere => ((string?)null, "nameSize"),
            _ => throw new ArgumentException("Only Verified and AlreadyThere outcomes get a file record.", nameof(outcome)),
        };
        var root = job.Root == DestRoot.Video ? "video" : "photo";
        return new FileRecord(Version, NewId(), machine, runId, atUtc, meta?.Kind ?? root, OffloadPaths.FileName(job.CardRelPath),
            job.Size, job.CardRelPath, root, job.DestPath, xxh, verify, job.CardMtimeUtc, meta?.CaptureUtc, meta?.TimeSource?.ToString(),
            meta?.Point?.Lat, meta?.Point?.Lon, meta?.TzId, meta?.LocalDate, meta?.Session?.SessionUtc, meta?.Session?.DroneSerial, meta?.Set);
    }

    public static FolderRecord Folder(FolderPlan folder, string source, string runId, string machine)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new FolderRecord(Version, NewId(), machine, runId, folder.FullPath, folder.Description, source,
                                folder.Centroid?.Lat, folder.Centroid?.Lon, folder.Start, folder.End, folder.TzId);
    }
}
