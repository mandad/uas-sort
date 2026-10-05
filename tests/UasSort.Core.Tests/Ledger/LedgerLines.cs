// tests/UasSort.Core.Tests/Ledger/LedgerLines.cs
using UasSort.Core.Ledger;
using UasSort.Core;

namespace UasSort.Core.Tests.Ledger;

/// <summary>Ledger record builders with the Ref §11 example values.</summary>
internal static class LedgerLines
{
    public const string Folder = @"C:\Lib\UAS Videos\.uas-sort";
    public static readonly RunCard Card = new("1A2B3C4D", null, "exFAT", "FC9113", "9f3c0a6d12e4b7a1");

    public static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    public static FileRecord FileRec(string id, string name, long size, string dest, string verify = "unbuffered",
        DateTime? at = null, string machine = "DESKTOP-A", double? lat = null, double? lon = null, string? tz = null,
        string? set = null, DateTime? captureUtc = null, string root = "video", string kind = "video",
        DateTime? sessionUtc = null, string? serial = null, string? xxh128 = "5e0c0000000000000000000000000001", string run = "8f1c0001")
        => new(1, id, machine, run, at ??Utc(2026, 9, 27, 21, 7, 2), kind, name, size, "DCIM/DJI_001/" + name, root, dest,
               xxh128, verify, Utc(2026, 9, 27, 18, 8, 1), captureUtc, captureUtc is null ? null : "Mvhd", lat, lon, tz,
               captureUtc is { } c ? DateOnly.FromDateTime(c) : null, sessionUtc, serial, set);

    public static FolderRecord FolderRec(string id, string path, string desc, DateOnly start, DateOnly end, string tz,
        double? lat = null, double? lon = null, string source = "created", string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", path, desc, source, lat, lon, start, end, tz);

    public static SeenRecord Seen(string id, string name, long size, DateTime at, string? set = null, string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", at, name, size, "DCIM/DJI_001/" + name, null, "New", "unticked", set);

    public static DecisionRecord Decision(string id, string name, long size, string kind = "assumedImported",
        DateTime? at = null, string? set = null, string machine = "DESKTOP-A")
        => new(1, id, machine, "8f1c0001", at ?? Utc(2026, 9, 27, 21, 40), kind, name, size, "DCIM/DJI_001/" + name, null,
               "confirmed by you", set);

    public static RevokeRecord Revoke(string id, string decisionId, string machine = "LAPTOP-B")
        => new(1, id, machine, Utc(2026, 10, 5, 2), decisionId);

    public static RunRecord Run(string id, string run, DateTime start, DateTime end, string videoRoot, string photoRoot,
        string verdict = "SafeWithAssumptions", string machine = "DESKTOP-A")
        => new(1, id, machine, run, start, end, "0.1.0", Card, new RunRoots(videoRoot, photoRoot), verdict,
               ImmutableDictionary<string, int>.Empty.Add("VerifiedThisRun", 17).Add("AssumedByRule", 40));

    public static TornRecord Torn(string id, int line, string machine = "DESKTOP-A")
        => new(1, id, machine, Utc(2026, 10, 6, 18, 2, 11), line);

    public static CardDeleteRecord CardDelete(string id, string name, long size)
        => new(1, id, "DESKTOP-A", "c4e20001", Utc(2026, 10, 12, 19, 30, 5), name, size, "DCIM/DJI_001/" + name,
               "DCIM/DJI_001/" + name, Utc(2026, 7, 26, 3, 26, 55), "InLedger", "in the history, verified", "beforeDate", Card, null);

    public static PhotoDeleteRecord PhotoDelete(string id, string name, long size, string evidence = "lightroom", string? set = null,
                                                DateTime? at = null, string mode = "verify")
        => new(1, id, "DESKTOP-A", "p7a10001", at ?? Utc(2026, 10, 4, 20, 15), name, size,
               @"C:\Lib\UAS Videos\Picture Offload\" + (set is null ? "" : set + @"\") + name, Utc(2026, 6, 1, 20, 10), set,
               evidence, mode, new DateOnly(2026, 6, 30));

    public static string Line(LedgerRecord r) => LedgerCodec.Serialize(r);

    /// <summary>Every record as one line, each terminated by '\n' (a cleanly closed file).</summary>
    public static string Text(params LedgerRecord[] records) => string.Concat(records.Select(r => Line(r) + "\n"));
}
