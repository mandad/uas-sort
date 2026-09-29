namespace UasSort.Platform.Tests;

/// <summary>A temp library + app-data + card layout with its Settings and GuardContext.</summary>
public sealed class TestEnv : IDisposable
{
    public const string Machine = "TESTPC";

    public TestEnv()
    {
        VideoRoot = Temp.Sub("video");
        PhotoRoot = Temp.Sub("photo");
        AppData = Temp.Sub("appdata");
        CardRoot = Temp.Sub("card");
        Settings = SettingsDefaults.Derive(Temp.Path) with
        {
            VideoRoot = VideoRoot, PhotoRoot = PhotoRoot, PreviousPhotoRoots = [], RootsConfirmed = true,
        };
    }

    public TempDir Temp { get; } = new();
    public PathFacts Facts { get; } = new();
    public string VideoRoot { get; }
    public string PhotoRoot { get; }
    public string AppData { get; }
    public string CardRoot { get; }
    public Settings Settings { get; }

    public GuardContext Context(string? cardRoot = null) => GuardContexts.For(Settings, AppData, Machine, Facts, cardRoot);
    public string C(string path) => Facts.Canonical(path);
    public void Dispose() => Temp.Dispose();
}
