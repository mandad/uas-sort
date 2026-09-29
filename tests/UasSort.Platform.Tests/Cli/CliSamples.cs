using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

internal static class CliSamples
{
    public const string CouncilId = "DCIM/DJI_001/DJI_20260725232655_0117_D.MP4";
    public const string AnvilFirstId = "DCIM/DJI_001/DJI_20260726235645_0001_D.MP4";
    public const string ZacharId = "DCIM/DJI_001/DJI_20260927140127_0123_D.MP4";

    public static VideoJson Video(string id, NewnessStatus status = NewnessStatus.New, bool included = true, params string[] flags) =>
        new(id, status, included, new DateTime(2026, 7, 26, 3, 26, 55, DateTimeKind.Utc), new DateOnly(2026, 7, 25),
            TimeSource.Mvhd, flags);

    public static GroupJson CouncilAppend() =>
        new(CouncilId, GroupTargetKind.Append, @"2026\2026-07\2026-07-25 Council Road", Confidence.Medium,
            "different day, 34 mi from Council Road", new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26),
            new BoundaryJson(BoundaryCause.DayGap, null, 1488.5, 62),
            [Video(CouncilId, NewnessStatus.Imported, false, "ClockMismatch"), Video(AnvilFirstId)],
            [new DaySplitJson(AnvilFirstId, new DateOnly(2026, 7, 25), new DateOnly(2026, 7, 26), 33.7, true)],
            [IssueCode.MediumAppend, IssueCode.EmphasisedDaySplit]);

    public static GroupJson ZacharNewFolder() =>
        new(ZacharId, GroupTargetKind.NewFolder, @"2026\2026-09\2026-09-27", null, null,
            new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 27), null,
            Enumerable.Range(0, 13).Select(i => Video($"DCIM/DJI_001/DJI_20260927140127_{123 + i:0000}_D.MP4")).ToList(),
            [], [IssueCode.EmptyFolderName]);

    public static PlanDocument Document() =>
        new(1,
            new CardJson(@"E:\", new IdentityJson("1A2B3C4D", null, "exFAT", 256060514304), "FC9113", 214, "9f3c0a6d12e4b7a1"),
            new SettingsJson(@"C:\x\UAS Videos", @"C:\x\UAS Videos\Picture Offload", 50, 1),
            new ClockJson(ClockMode.Zone, "America/New_York", 13, new MismatchJson(25, ["America/Anchorage"])),
            new DateTime(2026, 9, 27, 18, 24, 16, DateTimeKind.Utc),
            [CouncilAppend(), ZacharNewFolder()],
            [new PhotoDayJson(new DateOnly(2026, 7, 25), "America/Anchorage", 12, 8, 4, "videos from this day are already in the library")],
            [new SetJson("DCIM/PANORAMA/001_0087", "001_0087 2026-09-27", SetResolution.DateSuffixed, 33)],
            [new OtherJson("DCIM/DJI_A001/x.MP4", EntryClass.Unknown, null)],
            [new IssueJson(IssueSeverity.Blocking, IssueCode.EmptyFolderName, ZacharId, "Name this folder", false)]);
}
