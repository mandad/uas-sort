// src/UasSort.Core/Planning/NewnessRules.Photo.cs
using UasSort.Core.Library;

namespace UasSort.Core.Planning;

public static partial class NewnessRules
{
    public static readonly TimeSpan NearWatermarkWindow = TimeSpan.FromMinutes(75);

    public static Newness Photo(MediaUnit unit, ItemTime t, LibraryIndex lib, LedgerSnapshot ledger, SetPlacement? placement,
                                IReadOnlySet<DateOnly> newVideoDays, DateTime? watermarkUtc)
    {
        var setName = unit is SetUnit su ? su.SetName : null;
        IReadOnlyList<CardEntry> files = unit switch
        {
            PhotoUnit p => [p.Primary],
            SetUnit s => s.Members,
            VideoUnit v => [v.Mp4],
        };
        var keys = files.Select(f => PlanKeys.Key(PlanKeys.FileName(f.RelPath), f.Size)).ToList();
        bool InSet(string? recordSet) => setName is null || string.Equals(recordSet, setName, StringComparison.OrdinalIgnoreCase);

        // 1. ledger file for every member, or an unrevoked decision for every member
        var recs = keys.Select(k => ledger.Files.TryGetValue(k, out var f) && InSet(f.Set) ? f : null).ToList();
        if (recs.All(r => r is not null))
        {
            var by = recs.Any(r => r!.Verify == VerifyKind.NameSize) ? Evidence.LedgerNameSize : Evidence.LedgerVerified;
            return new Imported(by, null, $"copied on {PlanText.ShortDate(DateOnly.FromDateTime(recs.Max(r => r!.AtUtc)))}");
        }
        var decs = keys.Select(k => ledger.Decisions.TryGetValue(k, out var d) && InSet(d.Set) ? d : null).ToList();
        if (decs.All(d => d is not null))
        {
            var last = decs.OrderByDescending(d => d!.AtUtc).First()!;
            return new Decided(last.Kind, last.AtUtc, last.Machine);
        }

        // 2. listed (sets: the set-folder rule), 2b. conflict (flat photos)
        if (unit is SetUnit)
        {
            if (placement is { Resolution: SetResolution.Imported })
                return new Imported(Evidence.LibraryNameSize, null, $"in library: {placement.FolderName}");
        }
        else
        {
            var listed = lib.Match(keys[0]);
            if (!listed.IsEmpty) return new Imported(Evidence.LibraryNameSize, null, $"in library: {listed[0].FullPath}");
            if (FindConflict(keys[0], lib, ledger) is { } c) return c;
        }

        // 3. seen, without file or decision
        if (keys.All(ledger.Seen.ContainsKey))
            return new IsNew(NewReason.SeenNotCopied, keys.Max(k => ledger.Seen[k]));

        // 4. after the watermark, or no watermark
        if (watermarkUtc is not { } wm || t.CaptureUtc > wm)
            return new IsNew(NewReason.AfterWatermark, null);

        // 5. drone-clock time within 75 min before the watermark
        if (IsDroneClock(t.Source) && wm - t.CaptureUtc <= NearWatermarkWindow)
            return new IsNew(NewReason.NearWatermark, null);

        // 6. the day has a New video
        if (newVideoDays.Contains(t.LocalDate))
            return new IsNew(NewReason.DayHasNewVideos, null);

        // 7. probably imported
        var hasLibraryVideos = lib.Folders.Any(f => f.DaysIn(t.TzId).Contains(t.LocalDate));
        var wmDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(wm, PlanText.Zone(t.TzId)));
        return new ProbablyImported(hasLibraryVideos
            ? "videos from this day are already in the library"
            : $"photo-only day before the last imported video ({PlanText.ShortDate(wmDay)})");
    }

    private static bool IsDroneClock(TimeSource s) =>
        s is TimeSource.DroneClockSiteLocal or TimeSource.DroneClockZone or TimeSource.DroneClockSample or TimeSource.DroneClockSetting;
}
