// src/UasSort.Core/Cleanup/Photos/PhotoCleanupVerifier.cs
namespace UasSort.Core.Cleanup;

/// <summary>Verify mode (spec 2026-10-04 §4): is a Picture Offload row safely in the Lightroom library (a hyperlapse: its result video in
/// the video library)? Opens nothing: it works on stamps already read and on the ledger. Photos and frames are matched against Lightroom
/// DNGs only; a JPG in Lightroom can only confirm a stitched panorama.</summary>
public static class PhotoCleanupVerifier
{
    public static readonly TimeSpan HyperlapseWindow = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PanoramaStitchWindow = TimeSpan.FromMinutes(2);

    /// <summary>PhotoRootMembers: every shot in Picture Offload (photo units' primaries and set frames, with or without a stamp; JPG twins
    /// excluded, they repeat their DNG) — the peers of the same-second rule. FlatPhotos: the photo units, where a stitched panorama is
    /// looked for.</summary>
    public sealed record Context(LightroomIndex Index, LedgerSnapshot Ledger, ImmutableArray<PhotoMember> PhotoRootMembers,
                                 ImmutableArray<PhotoItem> FlatPhotos);

    public static Context ContextFor(IReadOnlyList<PhotoItem> items, LightroomIndex index, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(ledger);
        return new Context(index, ledger,
            [.. items.SelectMany(i => i.Kind == PhotoItemKind.Photo ? ImmutableArray.Create(i.Primary) : i.Members)],
            [.. items.Where(i => i.Kind == PhotoItemKind.Photo)]);
    }

    public static PhotoVerification Verify(PhotoItem item, Context ctx)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(ctx);
        if (item.Kind == PhotoItemKind.Photo)
        {
            var (ok, text) = Match(item.Primary, ctx);                          // a DNG+JPG pair: the DNG decides
            return new PhotoVerification(ok, PhotoEvidence.Lightroom, text);
        }
        var frames = item.Members.Select(m => Match(m, ctx)).ToList();
        if (frames.TrueForAll(f => f.Ok)) return new PhotoVerification(true, PhotoEvidence.Lightroom, $"every frame is in Lightroom ({frames.Count})");
        if (item.SetKind == PhotoSetKind.Hyperlapse)
        {
            var (video, why) = HyperlapseVideo(item, ctx.Ledger);
            return video is not null
                ? new PhotoVerification(true, PhotoEvidence.HyperlapseResult, $"hyperlapse result video is in the library: {PathRules.FileName(video.Dest)}")
                : new PhotoVerification(false, PhotoEvidence.HyperlapseResult, why);
        }
        if (item.SetKind == PhotoSetKind.Panorama)
        {
            var (stitched, why) = StitchedImage(item, ctx);
            return stitched is not null
                ? new PhotoVerification(true, PhotoEvidence.PanoramaStitch, $"stitched panorama {stitched.Name} is in Lightroom")
                : new PhotoVerification(false, PhotoEvidence.PanoramaStitch, FramesText(frames, why));
        }
        return new PhotoVerification(false, PhotoEvidence.Lightroom, FramesText(frames, null));
    }

    /// <summary>One shot against the index (Review Focus 3): equal sub-seconds when both have them; otherwise every Picture Offload shot of
    /// that second and Model needs its own Lightroom DNG (a burst can't be told apart without sub-seconds), and when a shot of that second
    /// couldn't be read (no stamp; found by its ledger capture time) only an exact sub-second match counts. allowJpg: the stitched
    /// panorama only (spec §4: "a Lightroom DNG/JPG").</summary>
    internal static (bool Ok, string Text) Match(PhotoMember m, Context ctx, bool allowJpg = false)
    {
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(ctx);
        if (m.Stamp is not { } stamp)
            return (false, m.IsCloudOnly ? PhotoCleanupRules.CloudOnlyCantCheck : m.DateProblem ?? "its capture time or camera model couldn't be read");
        var lr = ctx.Index.SameSecond(stamp).Where(p => allowJpg || p.IsDng).ToList();
        if (lr.Find(p => p.Stamp.BothHaveSubSec(stamp) && p.Stamp.SameShot(stamp)) is { } exact) return (true, InLightroom(ctx, exact));
        var known = ctx.PhotoRootMembers.Select(p => p.Stamp).OfType<ExifStamp>().Where(s => s.SameSecondAndModel(stamp)).ToList();
        var unknown = m.CaptureUtc is { } utc
            ? ctx.PhotoRootMembers.Count(p => p.Stamp is null && p.CaptureUtc is { } u && WholeSecond(u) == WholeSecond(utc))
            : 0;
        var loose = lr.Where(p => p.Stamp.SameShot(stamp) && !known.Exists(r => r.BothHaveSubSec(p.Stamp) && r.SameShot(p.Stamp))).ToList();
        if (loose.Count == 0)
            return (false, lr.Count == 0 ? "not found in Lightroom" : "not found in Lightroom (another shot in the same second is)");
        if (unknown > 0)
            return (false, $"{known.Count + unknown} shots in the same second, {unknown} of them couldn't be read — can't tell which");
        var peers = Math.Max(1, known.Count(r => !lr.Exists(p => p.Stamp.BothHaveSubSec(r) && p.Stamp.SameShot(r))));
        return loose.Count >= peers
            ? (true, InLightroom(ctx, loose[0]))
            : (false, $"{peers} shots in the same second; only {loose.Count} in Lightroom — can't tell which");
    }

    /// <summary>Spec §4 hyperlapse rule (branch-2 ruling): the offload records stills without a session or serial (only MP4s carry a
    /// SessionKey), so the frames link to DJI's result video through their offload run — a ledger video (a file record with the video
    /// root) from the same Run as the frames' file records (the same card) shot within the set's span ± 2 min. The spec's session and
    /// serial paths stay as additional ways: a session matching a member's always counts; a video in the window on the same, known
    /// drone serial counts. Frames without any file record (not offloaded by uas-sort) can't be linked.</summary>
    internal static (LedgerFile? Video, string Why) HyperlapseVideo(PhotoItem set, LedgerSnapshot ledger)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(ledger);
        var records = set.Members.Select(m => m.Ledger).OfType<LedgerFile>().ToList();
        if (records.Count == 0) return (null, "these frames have no offload record in the history — their result video can't be identified");
        var runs = records.Select(r => r.Run).ToHashSet(StringComparer.Ordinal);
        var sessions = records.Select(r => r.Session).OfType<SessionKey>().ToList();
        var times = set.Members.Select(m => m.CaptureUtc).OfType<DateTime>().ToList();
        var serial = sessions.Select(s => s.DroneSerial).FirstOrDefault(s => s is not null);
        var inWindowUnlinked = false;
        foreach (var v in ledger.Files.Values.Where(f => f.Root == DestRoot.Video).OrderBy(f => f.Dest, StringComparer.OrdinalIgnoreCase))
        {
            if (v.Session is { } vs && sessions.Exists(s => s.SameSession(vs))) return (v, "");
            if (times.Count == 0 || v.CaptureUtc is not { } at || at < times.Min() - HyperlapseWindow || at > times.Max() + HyperlapseWindow) continue;
            if (runs.Contains(v.Run)) return (v, "");
            if (serial is not null && v.Session is { DroneSerial: { } videoSerial } && string.Equals(serial, videoSerial, StringComparison.Ordinal))
                return (v, "");
            inWindowUnlinked = true;
        }
        return (null, inWindowUnlinked
            ? "a video in the library was shot then, but it came from another offload — can't tell it is this hyperlapse's result"
            : "hyperlapse result video not in the library");
    }

    /// <summary>The stitched panorama (resolved ambiguity 6; UNVERIFIED against real DJI output, acceptance step 3): the only JPG-only
    /// photo of the same Model shot within [first frame, last frame + 2 min], panorama-shaped, and in Lightroom (DNG or JPG).</summary>
    internal static (PhotoMember? Stitched, string Why) StitchedImage(PhotoItem set, Context ctx)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(ctx);
        var frames = set.Members.Select(m => m.Stamp).OfType<ExifStamp>().ToList();
        if (frames.Count == 0) return (null, "no stitched panorama can be looked for (the frames' capture times couldn't be read)");
        var first = frames.Min(s => s.Second);
        var last = frames.Max(s => s.Second);
        var jpgOnly = ctx.FlatPhotos.Where(f => f.Members.Length == 1 && PhotoCleanupRules.IsJpg(f.Primary.Name)).Select(f => f.Primary).ToList();
        // Branch-2 ruling: a JPG without a stamp (cloud-only, unreadable, no DateTimeOriginal) dated within the frames' local dates ± 1 day,
        // or with no date, may be this set's real stitched image: then another panorama's stitch must never verify the set.
        var days = set.Members.Select(m => m.LocalDate).OfType<DateOnly>().ToList();
        if (jpgOnly.Exists(p => p.Stamp is null
                                && (p.LocalDate is not { } d || days.Count == 0 || (d >= days.Min().AddDays(-1) && d <= days.Max().AddDays(1)))))
            return (null, "a JPG next to the frames couldn't be read — can't tell which is the stitched panorama");
        var near = jpgOnly
            .Where(p => p.Stamp is { } s && string.Equals(s.Model, frames[0].Model, StringComparison.OrdinalIgnoreCase)
                        && s.Second >= first && s.Second <= last + PanoramaStitchWindow)
            .ToList();
        if (near.Count == 0) return (null, "no stitched panorama next to the frames");
        if (near.Count > 1) return (null, $"{near.Count} JPG photos next to the frames — can't tell which is the stitched panorama");
        var candidate = near[0];
        if (!PhotoCleanupRules.LooksLikePanorama(candidate.Pixels)) return (null, $"{candidate.Name} next to the frames is not panorama-shaped");
        var (ok, text) = Match(candidate, ctx, allowJpg: true);
        return ok ? (candidate, "") : (null, $"stitched panorama {candidate.Name}: {text}");
    }

    private static DateTime WholeSecond(DateTime t) => new(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, t.Kind);

    private static string InLightroom(Context ctx, LightroomPhoto p) => "in Lightroom: " + Path.GetRelativePath(ctx.Index.Folder, p.FullPath);

    private static string FramesText(List<(bool Ok, string Text)> frames, string? lead)
    {
        var text = $"{frames.Count(f => !f.Ok)} of {frames.Count} frames not confirmed: {frames.First(f => !f.Ok).Text}";
        return lead is null ? text : $"{lead}; {text}";
    }
}
