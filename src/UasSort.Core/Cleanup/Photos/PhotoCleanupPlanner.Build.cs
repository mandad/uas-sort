// src/UasSort.Core/Cleanup/Photos/PhotoCleanupPlanner.Build.cs
namespace UasSort.Core.Cleanup;

public static partial class PhotoCleanupPlanner
{
    /// <summary>The dates the Lightroom index must cover: the first eligible date through the cutoff; null when nothing is eligible.</summary>
    public static (DateOnly From, DateOnly To)? VerifyRange(PhotoSurvey survey, DateOnly cutoff)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var eligible = survey.Items.Where(i => Eligibility(i, cutoff).Eligibility == PhotoEligibility.Eligible).ToList();
        return eligible.Count == 0 ? null : (eligible.Min(i => i.FirstDate!.Value), cutoff);
    }

    /// <summary>Spec 2026-10-04 §3–§4: an item is eligible when every member was shot on or before the cutoff; a set straddling it and an
    /// item with an unknown date are listed as not eligible; later items are left out. Verify mode reads the stamps it still needs
    /// (local files only) and verifies every eligible row against the index.</summary>
    public static PhotoCleanupPlan Build(PhotoSurvey survey, PhotoCleanupRequest request, LightroomIndex? index, PhotoExifCache exif,
                                         IProgress<PhotoScanProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(exif);
        if (request.Mode == PhotoCleanupMode.Verify && index is null) throw new ArgumentException("Verify mode needs a Lightroom index", nameof(index));

        var results = survey.Items.Select(i => Eligibility(i, request.Cutoff)).ToList();
        var items = survey.Items.ToList();
        PhotoCleanupVerifier.Context? verify = null;
        if (request.Mode == PhotoCleanupMode.Verify)
        {
            var eligibleFirst = items.Where((_, n) => results[n].Eligibility == PhotoEligibility.Eligible).Select(i => i.FirstDate!.Value).ToList();
            var lo = (eligibleFirst.Count == 0 ? request.Cutoff : eligibleFirst.Min()).AddDays(-1);
            var hi = request.Cutoff.AddDays(1);
            bool Needed(int n) => results[n].Eligibility == PhotoEligibility.Eligible
                                  || (items[n].Kind == PhotoItemKind.Photo && items[n].Members.Any(m => m.LocalDate is { } d && d >= lo && d <= hi));
            var toRead = Enumerable.Range(0, items.Count).Count(Needed);
            var done = 0;
            for (var n = 0; n < items.Count; n++)
            {
                ct.ThrowIfCancellationRequested();
                if (!Needed(n)) continue;
                items[n] = Enrich(survey.PhotoRoot, items[n], exif);
                progress?.Report(new PhotoScanProgress("Reading photo details", ++done, toRead));
            }
            verify = PhotoCleanupVerifier.ContextFor(items, index!, survey.Ledger);
        }

        var rows = new List<PhotoRow>();
        var notEligible = new List<PhotoRow>();
        for (var n = 0; n < items.Count; n++)
        {
            var (eligibility, why) = results[n];
            switch (eligibility)
            {
                case PhotoEligibility.Eligible:
                    rows.Add(new PhotoRow(items[n], eligibility,
                                          verify is null ? PhotoVerification.DateMode : PhotoCleanupVerifier.Verify(items[n], verify), null));
                    break;
                case PhotoEligibility.DateUnknown or PhotoEligibility.StraddlesCutoff:
                    notEligible.Add(new PhotoRow(items[n], eligibility, PhotoVerification.DateMode, why));
                    break;
            }
        }
        return new PhotoCleanupPlan(Guid.NewGuid().ToString("N"), survey.PhotoRoot, request, [.. Order(rows)], [.. Order(notEligible)], survey.NotTouched)
        {
            Lightroom = verify is null ? null : index!.Summary(),
        };
    }

    internal static (PhotoEligibility Eligibility, string? Why) Eligibility(PhotoItem item, DateOnly cutoff)
    {
        if (!item.DateKnown) return (PhotoEligibility.DateUnknown, item.Members.First(m => m.LocalDate is null).DateProblem ?? "date unknown");
        if (item.LastDate!.Value <= cutoff) return (PhotoEligibility.Eligible, null);
        if (item.FirstDate!.Value <= cutoff)
            return (PhotoEligibility.StraddlesCutoff, item.Kind == PhotoItemKind.Set
                ? $"part of the set was shot after {CleanupFormat.MonthDay(cutoff)}"
                : $"one of its files was shot after {CleanupFormat.MonthDay(cutoff)}");     // a DNG+JPG pair is one unit: never split
        return (PhotoEligibility.AfterCutoff, null);
    }

    /// <summary>Reads the stamp (and pixel size, for the stitched-panorama test) of local members that don't have one yet; never a
    /// cloud-only member (Review Focus 1).</summary>
    private static PhotoItem Enrich(string root, PhotoItem item, PhotoExifCache exif)
        => item with
        {
            Members = [.. item.Members.Select(m =>
            {
                if (m.Stamp is not null || m.IsCloudOnly) return m;
                var read = exif.Read(PhotoCleanupPaths.Full(root, m.RelPath), m.Size, m.MtimeUtc, m.Attributes);
                return m with { Stamp = read.Stamp, Pixels = PhotoCleanupRules.PixelsOf(read.Info) };
            })],
        };

    private static IEnumerable<PhotoRow> Order(IEnumerable<PhotoRow> rows)
        => rows.OrderBy(r => r.Item.FirstDate ?? DateOnly.MaxValue).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase);
}
