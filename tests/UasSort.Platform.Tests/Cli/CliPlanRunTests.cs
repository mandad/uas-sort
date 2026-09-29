using System.Text.Json;
using UasSort.Cli;

namespace UasSort.Platform.Tests.Cli;

public sealed class CliPlanRunTests : IDisposable
{
    private readonly TestTempDir _temp = new();
    private readonly string _dir;
    private readonly string _card;
    private readonly string _video;
    private readonly string _photo;
    private readonly string _settingsDir;
    private readonly string _appData = KnownFolders.AppDataDir();   // %LOCALAPPDATA%\uas-sort, the folder WindowsCliHost names

    public CliPlanRunTests()
    {
        _dir = _temp.FullPath;
        _card = Path.Combine(_dir, "card");
        _video = Path.Combine(_dir, "video");
        _photo = Path.Combine(_dir, "photo");
        _settingsDir = Path.Combine(_dir, "settings");
        CliTestCard.Write(_card);
        Directory.CreateDirectory(Path.Combine(_video, ".uas-sort"));   // Check() → Empty: read, never written
        Directory.CreateDirectory(_photo);
        Directory.CreateDirectory(_settingsDir);
    }

    public void Dispose() => _temp.Dispose();

    private string[] Args(params string[] extra) =>
        ["plan", "--card", _card, "--video-root", _video, "--photo-root", _photo,
         "--settings", Path.Combine(_settingsDir, "settings.json"), .. extra];

    private Dictionary<string, IReadOnlyList<string>> Snapshot() => new()
    {
        ["card"] = TreeSnapshot.Take(_card),
        ["video"] = TreeSnapshot.Take(_video),
        ["photo"] = TreeSnapshot.Take(_photo),
        ["settings"] = TreeSnapshot.Take(_settingsDir),
        ["appData"] = TreeSnapshot.Take(_appData),
    };

    private static async Task<(int Code, string Out, string Err)> Run(string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = await PlanCommand.RunAsync(args, stdout, stderr, new WindowsCliHost(), TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private void AssertNothingWritten(Dictionary<string, IReadOnlyList<string>> before)
    {
        var after = Snapshot();
        foreach (var (name, listing) in before)
            Assert.True(listing.SequenceEqual(after[name]),
                $"{name} changed:\n- {string.Join("\n- ", listing.Except(after[name]))}\n+ {string.Join("\n+ ", after[name].Except(listing))}");
    }

    [Fact]
    public async Task PlanJson_OnATempCard_PrintsThePlan_AndWritesNothing()
    {
        var before = Snapshot();

        var (code, stdout, stderr) = await Run(Args("--json"));

        Assert.True(code == 0, $"exit {code}; stderr:\n{stderr}");
        AssertNothingWritten(before);
        Assert.Contains("derived defaults", stderr, StringComparison.Ordinal);
        Assert.Contains("scan + plan:", stderr, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal(CliTestCard.FileCount, root.GetProperty("card").GetProperty("files").GetInt32());
        Assert.Matches("^[0-9a-fA-F]{16}$", root.GetProperty("card").GetProperty("inventoryHash").GetString());
        Assert.Equal(_video, root.GetProperty("settings").GetProperty("videoRoot").GetString());

        var group = Assert.Single(root.GetProperty("groups").EnumerateArray());
        Assert.Equal("NewFolder", group.GetProperty("target").GetString());
        Assert.StartsWith(@"2026\2026-09\2026-09-27", group.GetProperty("relPath").GetString(), StringComparison.Ordinal);
        var videos = group.GetProperty("videos").EnumerateArray().ToList();
        Assert.Equal(CliTestCard.Clips, videos.Select(v => Path.GetFileName(v.GetProperty("id").GetString()!)).ToArray());
        Assert.All(videos, v =>
        {
            Assert.Equal("New", v.GetProperty("status").GetString());
            Assert.True(v.GetProperty("included").GetBoolean());
            Assert.Equal("2026-09-27", v.GetProperty("localDate").GetString());
            Assert.Equal("Mvhd", v.GetProperty("timeSource").GetString());
        });
        Assert.Contains(root.GetProperty("other").EnumerateArray(),
                        o => o.GetProperty("relPath").GetString() == "MISC/FC9113.db" && o.GetProperty("class").GetString() == "Skip");
    }

    [Fact]
    public async Task PlanText_OnATempCard_PrintsTheGroupBlock_AndWritesNothing()
    {
        var before = Snapshot();
        var (code, stdout, stderr) = await Run(Args());
        Assert.True(code == 0, $"exit {code}; stderr:\n{stderr}");
        AssertNothingWritten(before);
        Assert.Contains(@"NEW FOLDER 2026\2026-09\2026-09-27", stdout, StringComparison.Ordinal);
        Assert.Contains("3 clips · Sep 27", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFolderWithoutDcim_IsRefused_Exit1_AndWritesNothing()
    {
        string notACard = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(notACard);
        var before = Snapshot();
        var (code, stdout, stderr) = await Run(["plan", "--card", notACard, "--video-root", _video, "--photo-root", _photo,
                                                "--settings", Path.Combine(_settingsDir, "settings.json")]);
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: No DCIM folder here", stderr, StringComparison.Ordinal);
        AssertNothingWritten(before);
    }

    [Fact]
    public async Task TheVideoRootAsCard_IsRefused_Exit1()
    {
        Directory.CreateDirectory(Path.Combine(_video, "DCIM", "DJI_001"));   // anchors at the video root itself
        var (code, stdout, stderr) = await Run(["plan", "--card", _video, "--video-root", _video, "--photo-root", _photo,
                                                "--settings", Path.Combine(_settingsDir, "settings.json")]);
        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("refused: This is part of your library", stderr, StringComparison.Ordinal);
    }
}
