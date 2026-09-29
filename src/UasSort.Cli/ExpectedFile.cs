using System.Text.Json;

namespace UasSort.Cli;

/// <summary>Parses and validates tests/acceptance/first-card-expected.json (Ref §4.5).</summary>
internal static class ExpectedFile
{
    public const int MaxBytes = 1_000_000;

    public static ExpectedFileJson Parse(string json)
    {
        ExpectedFileJson? file;
        try { file = JsonSerializer.Deserialize(json, CliJsonContext.Default.ExpectedFileJson); }
        catch (JsonException ex) { throw new FormatException($"the expected file is not valid JSON: {ex.Message}", ex); }

        if (file is null || file.Folders is not { Count: > 0 } folders)
            throw new FormatException("the expected file has no folders (\"folders\": [ { \"relPath\": …, \"clips\": [ … ] } ])");
        foreach (var f in folders)
        {
            if (string.IsNullOrWhiteSpace(f.RelPath))
                throw new FormatException("an expected folder has an empty relPath");
            if (f.Clips is not { Count: > 0 })
                throw new FormatException($"expected folder {f.RelPath} has no clips");
            foreach (var clip in f.Clips)
                if (string.IsNullOrWhiteSpace(clip) || clip.Contains('/', StringComparison.Ordinal) || clip.Contains('\\', StringComparison.Ordinal))
                    throw new FormatException($"expected folder {f.RelPath}: clip '{clip}' must be a bare file name such as DJI_20261004163012_0151_D.MP4");
        }
        return file;
    }
}
