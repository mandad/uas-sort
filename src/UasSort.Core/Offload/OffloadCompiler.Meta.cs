using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>What a ledger `file` record needs beyond the CopyJob (Ref §11 field list).</summary>
public sealed record JobMeta(string Kind, DateTime? CaptureUtc, TimeSource? TimeSource, GeoPoint? Point, string? TzId,
                             DateOnly? LocalDate, SessionKey? Session, string? Set);

public static partial class OffloadCompiler
{
    public static ImmutableDictionary<string, JobMeta> Describe(Plan plan, OffloadBatch batch)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(batch);
        var items = plan.Base.Items.ToDictionary(i => i.Raw.Unit.Id);
        var meta = ImmutableDictionary.CreateBuilder<string, JobMeta>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs)
        {
            if (!items.TryGetValue(job.Item, out var item)) continue;
            string kind;
            string? set = null;
            switch (item.Raw.Unit)
            {
                case VideoUnit:
                    kind = "video";
                    break;
                case PhotoUnit p:
                    kind = p.JpgTwin is { } twin && string.Equals(twin.RelPath, job.CardRelPath, StringComparison.OrdinalIgnoreCase)
                        ? "twin" : "photo";
                    break;
                case SetUnit s:
                    kind = "setMember";
                    set = s.SetName;
                    break;
                default:
                    continue;
            }
            meta[job.CardRelPath] = new JobMeta(kind, item.Time.CaptureUtc, item.Time.Source, item.Gps?.Point, item.Time.TzId,
                                                item.Time.LocalDate, item.Session, set);
        }
        return meta.ToImmutable();
    }

    public static ImmutableHashSet<string> NewFolderDirs(OffloadBatch batch, string videoRoot)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var dirs = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in batch.Jobs.Where(j => j.CreatesFolder))
        {
            var dir = OffloadPaths.DirectoryOf(job.DestPath);
            if (job.Root == DestRoot.Video) dirs.UnionWith(OffloadPaths.NewFolderDirs(videoRoot, dir));
            else dirs.Add(dir);
        }
        return dirs.ToImmutable();
    }
}
