using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace UasSort.Cli;

/// <summary>`uas-sort-cli plan` (Ref §4.5): validate → scan (ledger read only) → Prepare → Derive with no edits → print.</summary>
internal static class PlanCommand
{
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, ICliHost host,
                                           CancellationToken ct)
    {
        if (!CliArgs.TryParse(args, out var cli, out var argError))
        {
            stderr.WriteLine($"error: {argError}");
            stderr.WriteLine(CliArgs.Usage);
            return 2;
        }

#pragma warning disable CA1031 // CLI boundary: every failure other than a refused source is exit code 2 with its message
        try
        {
            var watch = Stopwatch.StartNew();
            ExpectedFileJson? expected = cli.ExpectPath is { } expectPath
                ? ExpectedFile.Parse(host.ReadExpectFile(expectPath))
                : null;

            var load = host.LoadSettings(cli.SettingsPath);
            // Part 05's meaning of Recovered (registry decision 46): true only for an unreadable file; a missing file gives the
            // derived defaults with Recovered = false and RootsConfirmed = false.
            if (load.Recovered)
                stderr.WriteLine("settings: the settings file is unreadable; using the derived defaults (nothing was written)");
            else if (!load.Settings.RootsConfirmed)
                stderr.WriteLine("settings: roots not confirmed (settings file missing, so the derived defaults are used, or Setup not finished; nothing was written)");
            var settings = Effective(load.Settings, cli);
            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"roots: video {settings.VideoRoot} · photo {settings.PhotoRoot} · R {settings.RadiusMiles} mi · G {settings.GapDays}"));

            (CardSource? Source, string? Refusal) check = host.Validate(cli.Card, settings) switch
            {
                SourceOk ok => (ok.Source, null),
                SourceRefused refused => (null, refused.Reason),
            };
            if (check.Source is not { } source)
            {
                stderr.WriteLine($"refused: {check.Refusal}");
                return 1;
            }

            var scan = await host.ScanAsync(source, settings, new StderrProgress(stderr), ct).ConfigureAwait(false);
            var plan = host.Plan(scan, new Tuning(settings.RadiusMiles, settings.GapDays), ct);
            var doc = PlanDocumentMapper.Map(plan, host.IdentityFor(source.Root) ?? source.Identity);

            if (cli.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(doc, CliJsonContext.Default.PlanDocument));
            }
            else
            {
                PlanTextRenderer.Render(doc, stdout);
            }
            if (expected is not null)
                ExpectDiff.Write(ExpectDiff.Compare(expected, doc.Groups), cli.Json ? stderr : stdout);

            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture, $"scan + plan: {watch.Elapsed.TotalSeconds:0.0} s"));
            return 0;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("error: cancelled");
            return 2;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
#pragma warning restore CA1031
    }

    public static Settings Effective(Settings loaded, CliArgs args) => loaded with
    {
        VideoRoot = args.VideoRoot is { } v ? Path.GetFullPath(v) : loaded.VideoRoot,
        PhotoRoot = args.PhotoRoot is { } p ? Path.GetFullPath(p) : loaded.PhotoRoot,
        RadiusMiles = args.RadiusMiles ?? loaded.RadiusMiles,
        GapDays = args.GapDays ?? loaded.GapDays,
    };
}

/// <summary>Scan progress on stderr, one line per phase; synchronous so lines never interleave with the plan.</summary>
internal sealed class StderrProgress(TextWriter stderr) : IProgress<ScanProgress>
{
    private readonly Lock _gate = new();
    private ScanPhase? _last;

    public void Report(ScanProgress value)
    {
        lock (_gate)
        {
            if (_last == value.Phase) return;
            _last = value.Phase;
            stderr.WriteLine(string.Create(CultureInfo.InvariantCulture, $"scan: {value.Phase} ({value.Total} items)"));
        }
    }
}
