using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace UasSort.Cli;

/// <summary>The command line of <c>uas-sort-cli plan</c> (Ref §4.5). There is no other command, and no cleanup.</summary>
internal sealed record CliArgs(string Card, string? VideoRoot, string? PhotoRoot, double? RadiusMiles, int? GapDays,
                               string? SettingsPath, bool Json, string? ExpectPath)
{
    public const string Usage =
        "usage: uas-sort-cli plan --card <path> [--video-root <path>] [--photo-root <path>] [--radius-mi <5..100>]\n" +
        "                         [--gap-days <0..7>] [--settings <path>] [--json] [--expect <expected.json>]\n" +
        "exit codes: 0 = plan printed, 1 = card source refused, 2 = any other error";

    private static readonly string[] ValueOptions =
        ["--card", "--video-root", "--photo-root", "--radius-mi", "--gap-days", "--settings", "--expect"];

    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out CliArgs? parsed,
                                [NotNullWhen(false)] out string? error)
    {
        parsed = null;
        if (args.Count == 0) { error = "missing command 'plan'"; return false; }
        if (!string.Equals(args[0], "plan", StringComparison.Ordinal))
        {
            error = $"unknown command '{args[0]}' (the only command is 'plan')";
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool json = false;
        for (int i = 1; i < args.Count; i++)
        {
            string a = args[i];
            if (a == "--json")
            {
                if (json) { error = "--json given twice"; return false; }
                json = true;
                continue;
            }
            if (Array.IndexOf(ValueOptions, a) < 0) { error = $"unknown argument '{a}'"; return false; }
            if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"{a} needs a value";
                return false;
            }
            if (!values.TryAdd(a, args[++i])) { error = $"{a} given twice"; return false; }
        }

        if (!values.TryGetValue("--card", out var card) || string.IsNullOrWhiteSpace(card))
        {
            error = "--card <path> is required";
            return false;
        }

        double? radius = null;
        if (values.TryGetValue("--radius-mi", out var r))
        {
            if (!double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rv) ||
                !double.IsFinite(rv) || rv < 5 || rv > 100)
            {
                error = $"--radius-mi must be a number from 5 to 100 (got '{r}')";
                return false;
            }
            radius = rv;
        }

        int? gap = null;
        if (values.TryGetValue("--gap-days", out var g))
        {
            if (!int.TryParse(g, NumberStyles.None, CultureInfo.InvariantCulture, out var gv) || gv > 7)
            {
                error = $"--gap-days must be a whole number from 0 to 7 (got '{g}')";
                return false;
            }
            gap = gv;
        }

        parsed = new CliArgs(card, values.GetValueOrDefault("--video-root"), values.GetValueOrDefault("--photo-root"),
                             radius, gap, values.GetValueOrDefault("--settings"), json, values.GetValueOrDefault("--expect"));
        error = null;
        return true;
    }
}
