using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class PlanCommandTests
{
    private static Settings Defaults() => new(
        Schema: 1, VideoRoot: @"C:\Users\x\Pictures\UAS Videos", PhotoRoot: @"C:\Users\x\Pictures\UAS Videos\Picture Offload",
        PreviousPhotoRoots: ImmutableArray<string>.Empty, RadiusMiles: 50, GapDays: 1, DroneClockMode: StoredClockMode.Zone,
        DroneClockZone: "America/New_York", CopyJpgTwin: true,
        Map: new MapSettings("streets", "s", "sd", "sat", ImmutableDictionary<string, string>.Empty),
        Layout: new LayoutSettings(380, 0.45), RootsConfirmed: false);

    private sealed class FakeHost : ICliHost
    {
        // A missing settings file (registry decision 46): the derived defaults, Recovered = false, RootsConfirmed = false.
        public SettingsLoad Load { get; init; } = new(Defaults(), Recovered: false, CorruptCopyPath: null, RootsFromLastRun: null);
        public CardSourceCheck Check { get; init; } = new SourceRefused("No DCIM folder here");
        public Exception? ScanThrows { get; init; }
        public string ExpectText { get; init; } = "";
        public string? LoadedFrom { get; private set; } = "(not called)";
        public Settings? ValidatedWith { get; private set; }
        public bool Scanned { get; private set; }
        public string AppDataDir => @"C:\Users\x\AppData\Local\uas-sort";

        public SettingsLoad LoadSettings(string? settingsPath) { LoadedFrom = settingsPath; return Load; }
        public CardSourceCheck Validate(string cardPath, Settings settings) { ValidatedWith = settings; return Check; }
        public CardIdentity? IdentityFor(string cardRoot) => null;
        public Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct)
        {
            Scanned = true;
            throw ScanThrows ?? new InvalidOperationException("the fake host cannot scan");
        }
        public Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct) => throw new InvalidOperationException("not reached");
        public string ReadExpectFile(string path) => ExpectText;
    }

    private static async Task<(int Code, string Out, string Err)> Run(FakeHost host, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await PlanCommand.RunAsync(args, stdout, stderr, host, TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task BadArguments_Exit2_WithUsageOnStderr_AndTouchNothing()
    {
        var host = new FakeHost();
        var (code, stdout, stderr) = await Run(host, "plan", "--radius-mi", "500");
        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("usage: uas-sort-cli plan", stderr, StringComparison.Ordinal);
        Assert.Equal("(not called)", host.LoadedFrom);
    }

    [Fact]
    public async Task RefusedSource_Exit1_WithTheReasonOnStderr()
    {
        var host = new FakeHost { Check = new SourceRefused("This is part of your library (or a synced folder); uas-sort only offloads from cards.") };
        var (code, stdout, stderr) = await Run(host, "plan", "--card", @"C:\Users\x\Pictures\UAS Videos");
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: This is part of your library", stderr, StringComparison.Ordinal);
        Assert.False(host.Scanned);
    }

    [Fact]
    public async Task ScanIoError_Exit2()
    {
        var host = new FakeHost
        {
            Check = new SourceOk(new CardSource(@"E:\", null, IsBrowsedFolder: true, IsWriteProtected: false)),
            ScanThrows = new IOException("The device is not ready."),
        };
        var (code, stdout, stderr) = await Run(host, "plan", "--card", @"E:\");
        Assert.Equal(2, code);
        Assert.Empty(stdout);
        Assert.Contains("error: IOException: The device is not ready.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadExpectFile_Exit2_BeforeAnyScan()
    {
        var host = new FakeHost
        {
            Check = new SourceOk(new CardSource(@"E:\", null, true, false)),
            ExpectText = "{ \"folders\": [] }",
        };
        var (code, _, stderr) = await Run(host, "plan", "--card", @"E:\", "--expect", @"C:\e.json");
        Assert.Equal(2, code);
        Assert.Contains("no folders", stderr, StringComparison.Ordinal);
        Assert.False(host.Scanned);
    }

    [Fact]
    public async Task SettingsPath_IsPassedThrough_AndDefaultsAreAnnouncedOnStderr()
    {
        var host = new FakeHost();
        var (_, _, stderr) = await Run(host, "plan", "--card", @"E:\", "--settings", @"C:\s\settings.json");
        Assert.Equal(@"C:\s\settings.json", host.LoadedFrom);
        Assert.Contains("derived defaults", stderr, StringComparison.Ordinal);
        Assert.Contains("nothing was written", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreadableSettings_AreAnnounced_ConfirmedSettingsAreNot()
    {
        var unreadable = new FakeHost { Load = new(Defaults(), Recovered: true, CorruptCopyPath: null, RootsFromLastRun: null) };
        var (_, _, stderrUnreadable) = await Run(unreadable, "plan", "--card", @"E:\");
        Assert.Contains("settings file is unreadable; using the derived defaults", stderrUnreadable, StringComparison.Ordinal);

        var confirmed = new FakeHost { Load = new(Defaults() with { RootsConfirmed = true }, Recovered: false, CorruptCopyPath: null, RootsFromLastRun: null) };
        var (_, _, stderrConfirmed) = await Run(confirmed, "plan", "--card", @"E:\");
        Assert.DoesNotContain("settings:", stderrConfirmed, StringComparison.Ordinal);
        Assert.Contains("roots: video", stderrConfirmed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandLineValues_Win_OverSettings()
    {
        var host = new FakeHost();
        await Run(host, "plan", "--card", @"E:\", "--video-root", @"C:\t\video", "--photo-root", @"C:\t\photo",
                  "--radius-mi", "25", "--gap-days", "0");
        var s = host.ValidatedWith!;
        Assert.Equal(@"C:\t\video", s.VideoRoot);
        Assert.Equal(@"C:\t\photo", s.PhotoRoot);
        Assert.Equal(25, s.RadiusMiles);
        Assert.Equal(0, s.GapDays);
        Assert.Equal("America/New_York", s.DroneClockZone);   // everything else comes from settings
    }

    [Fact]
    public void Effective_KeepsSettingsWhenNoOverrides()
    {
        var s = Defaults();
        Assert.Equal(s, PlanCommand.Effective(s, new CliArgs(@"E:\", null, null, null, null, null, false, null)));
    }
}
