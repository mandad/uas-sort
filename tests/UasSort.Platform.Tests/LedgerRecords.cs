namespace UasSort.Platform.Tests;

internal static class LedgerRecords
{
    public static RunRecord Run(string machine, string run, string videoRoot = @"C:\v", string photoRoot = @"C:\p")
        => new(1, Guid.NewGuid().ToString("N"), machine, run,
               new DateTime(2026, 9, 27, 21, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 27, 21, 30, 0, DateTimeKind.Utc), "0.1.0",
               new RunCard("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1"), new RunRoots(videoRoot, photoRoot),
               "Safe", ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 1));

    public static string Line(LedgerRecord r) => LedgerCodec.Serialize(r);

    public static void Write(string path, params LedgerRecord[] records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Concat(records.Select(r => Line(r) + "\n")));
    }
}
