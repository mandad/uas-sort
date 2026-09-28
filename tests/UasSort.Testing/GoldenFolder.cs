using System.Text.Json;
using System.Text.RegularExpressions;

namespace UasSort.Testing;

/// <summary>
/// The optional real-file checks (Ref §13): <c>UASSORT_GOLDEN</c> names a folder of COPIES from a card. The folder is
/// refused if it could be user data: a volume root (a real card), anything inside a OneDrive, "UAS Videos" or
/// "Picture Offload" folder, the Pictures folder (the default library roots), the OneDrive roots, the library roots
/// configured in %LOCALAPPDATA%\uas-sort\settings.json, or %LOCALAPPDATA%\uas-sort itself.
/// Sync roots other than OneDrive are not detected here (spec UNVERIFIED list); OneDrive is covered by the env roots and folder names.
/// </summary>
public static partial class GoldenFolder
{
    public const string Variable = "UASSORT_GOLDEN";

    private static readonly string[] ForbiddenSegments = ["OneDrive", "UAS Videos", "Picture Offload"];

    /// <summary>Why <paramref name="fullPath"/> is refused, or null when it may be read.</summary>
    public static string? Refusal(string fullPath, IReadOnlyList<string> forbiddenRoots)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);
        string p = fullPath.Replace('/', '\\').TrimEnd('\\');
        if (VolumeRoot().IsMatch(p + "\\")) return "a volume root (a card?); point it at a folder of copies";
        foreach (string segment in p.Split('\\'))
        {
            foreach (string bad in ForbiddenSegments)
            {
                if (segment.Equals(bad, StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(bad + " - ", StringComparison.OrdinalIgnoreCase))
                    return $"inside a '{bad}' folder";
            }
        }
        foreach (string root in forbiddenRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string r = root.Replace('/', '\\').TrimEnd('\\');
            if (p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                return $"inside {r}";
        }
        return null;
    }

    /// <summary>
    /// The Pictures folder, the OneDrive roots from the environment, %LOCALAPPDATA%\uas-sort, and the library roots
    /// (videoRoot, photoRoot, previousPhotoRoots) configured in its settings.json when that file exists.
    /// </summary>
    public static IReadOnlyList<string> DefaultForbiddenRoots()
    {
        string appDir = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "uas-sort");
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetEnvironmentVariable("OneDrive") ?? "",
            Environment.GetEnvironmentVariable("OneDriveConsumer") ?? "",
            Environment.GetEnvironmentVariable("OneDriveCommercial") ?? "",
            appDir,
        };
        string settings = Path.Join(appDir, "settings.json");
        try
        {
#pragma warning disable RS0030 // Golden checks only: reads the settings file to learn the configured library roots (Ref §13)
            if (System.IO.File.Exists(settings))
                roots.AddRange(ConfiguredRoots(System.IO.File.ReadAllText(settings)));
#pragma warning restore RS0030
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return roots;
    }

    /// <summary>The string values of <c>videoRoot</c>, <c>photoRoot</c> and <c>previousPhotoRoots</c> in a settings.json text; empty when malformed.</summary>
    public static IReadOnlyList<string> ConfiguredRoots(string settingsJson)
    {
        ArgumentNullException.ThrowIfNull(settingsJson);
        var roots = new List<string>();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return roots;
            foreach (string name in (string[])["videoRoot", "photoRoot"])
            {
                if (doc.RootElement.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String
                    && e.GetString() is { } s)
                    roots.Add(s);
            }
            if (doc.RootElement.TryGetProperty("previousPhotoRoots", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in arr.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String && e.GetString() is { } s) roots.Add(s);
                }
            }
        }
        catch (JsonException)
        {
            return [];
        }
        return roots;
    }

    /// <summary>
    /// Full path of <paramref name="name"/> in the golden folder; null when the variable is unset or the file is absent.
    /// Throws when the folder is refused, so a misconfigured run fails loudly instead of reading user data.
    /// </summary>
    public static string? File(string name)
    {
        string? dir = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(dir)) return null;
        string full = Path.GetFullPath(dir);
        if (Refusal(full, DefaultForbiddenRoots()) is { } why)
            throw new InvalidOperationException($"{Variable} refused ({why}): {full}");
        string path = Path.Join(full, name);
#pragma warning disable RS0030 // Golden checks only: existence test of a copy under UASSORT_GOLDEN (Ref §13)
        return System.IO.File.Exists(path) ? path : null;
#pragma warning restore RS0030
    }

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\[^\\]+\\[^\\]+\\)$")]
    private static partial Regex VolumeRoot();
}
