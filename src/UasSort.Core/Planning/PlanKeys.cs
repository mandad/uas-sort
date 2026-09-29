namespace UasSort.Core.Planning;

/// <summary>Matching keys (Ref §7.1) for planning code: thin names over Part 02's FileKey/PathRules and Part 03's DroneStampParser,
/// so the planner's keys are exactly the keys Part 05 indexed.</summary>
public static class PlanKeys
{
    public static string NormName(string fileName) => FileKey.NormalizeName(fileName);

    public static FileKey Key(string fileName, long size) => FileKey.Of(fileName, size);

    public static string FileName(string path) => PathRules.FileName(path);

    public static DateTime? DjiStamp(string fileName) => DroneStampParser.FromFileName(fileName);
}
