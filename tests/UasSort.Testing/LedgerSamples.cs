// tests/UasSort.Testing/LedgerSamples.cs — owner Part 11 (registry decision 31)
namespace UasSort.Testing;

/// <summary>One ledger line of every record kind, format version 1, written with Part 05's LedgerCodec. The records describe a
/// 2025 Juneau trip, far from and long before any selftest or fixture card, so a ledger holding them changes no plan.</summary>
public static class LedgerSamples
{
    public static IReadOnlyList<string> AllRecordKindsV1()
    {
        const int v = LedgerCodec.Version;
        const string machine = "SELFTEST";
        const string run = "run-selftest-0001";
        const string name = "DJI_20250601120000_0001_D.MP4";
        const string src = "DCIM/DJI_001/" + name;
        const string still = "DJI_20250601121000_0002_D.DNG";
        const string folder = @"X:\UAS Videos\2025\2025-06\2025-06-01 Juneau";
        var at = new DateTime(2025, 6, 1, 22, 0, 0, DateTimeKind.Utc);
        var card = new RunCard("1A2B3C4D", "SELFTEST", "exFAT", "FC9113", "0000000000000000");
        LedgerRecord[] records =
        [
            new FileRecord(v, "sample-file", machine, run, at, "video", name, 1_048_576, src, "video", folder + @"\" + name,
                           "00000000000000000000000000000001", "unbuffered", at.AddHours(-2), at.AddHours(-2), "Mvhd", 58.3019, -134.4197,
                           "America/Juneau", new DateOnly(2025, 6, 1), at.AddHours(-2).AddMinutes(-5), "1581F0001", null),
            new FolderRecord(v, "sample-folder", machine, run, folder, "Juneau", "created", 58.3019, -134.4197,
                             new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 1), "America/Juneau"),
            new SeenRecord(v, "sample-seen", machine, run, at, still, 25_165_824, "DCIM/DJI_001/" + still, at.AddHours(-2), "New", "unticked", null),
            new DecisionRecord(v, "sample-decision", machine, run, at, "dismissed", still, 25_165_824, "DCIM/DJI_001/" + still,
                               at.AddHours(-2), "not needed", null),
            new RevokeRecord(v, "sample-revoke", machine, at.AddMinutes(1), "sample-decision"),
            new RunRecord(v, "sample-run", machine, run, at.AddMinutes(-10), at, "0.1.0", card,
                          new RunRoots(@"X:\UAS Videos", @"X:\UAS Videos\Picture Offload"), "Safe",
                          ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 1)),
            new TornRecord(v, "sample-torn", machine, at.AddMinutes(2), 7),
            new CardDeleteRecord(v, "sample-card-delete", machine, "run-selftest-0002", at.AddDays(1), name, 1_048_576, src, src,
                                 at.AddHours(-2), "InLedger", "in the history, verified", "beforeDate", card, null),
        ];
        return [.. records.Select(LedgerCodec.Serialize)];
    }
}
