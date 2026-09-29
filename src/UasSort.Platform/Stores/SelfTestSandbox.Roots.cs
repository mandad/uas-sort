namespace UasSort.Platform.Stores;

/// <summary>Ref §11 Selftest row / §13: the sandbox also holds the selftest's settings and app data, a temp video root whose
/// .uas-sort\ holds the test ledger, a photo root and the synthetic card (Part 01 already holds WebView2Folder there).
/// Part 11's part of Part 01's partial SelfTestSandbox (Task 01.9).</summary>
public sealed partial class SelfTestSandbox
{
    /// <summary>Create(Path.GetTempPath()), then the four roots of the full selftest.</summary>
    public static SelfTestSandbox Create()
    {
        var sandbox = Create(Path.GetTempPath());
#pragma warning disable RS0030 // IO layer: selftest sandbox, only under %TEMP%\uas-sort-selftest-<guid>
        foreach (var dir in new[] { sandbox.AppDataDir, sandbox.VideoRoot, sandbox.PhotoRoot, sandbox.CardRoot })
            Directory.CreateDirectory(dir);
#pragma warning restore RS0030
        return sandbox;
    }

    /// <summary>The App's app-data folder under --selftest (settings, drafts, reports, logs).</summary>
    public string AppDataDir => Path.Join(Root, "appdata");

    public string VideoRoot => Path.Join(Root, "video");

    public string PhotoRoot => Path.Join(Root, "photo");

    public string CardRoot => Path.Join(Root, "card");

    /// <summary>Writes one file below Root (creating its folders); a path that leaves Root throws before any IO.</summary>
    public void WriteFile(string relativeToRoot, ReadOnlySpan<byte> content)
    {
        var full = Path.GetFullPath(Path.Join(Root, relativeToRoot));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selftest write outside the sandbox: " + relativeToRoot);
#pragma warning disable RS0030 // IO layer: selftest sandbox, only under %TEMP%\uas-sort-selftest-<guid>
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
#pragma warning restore RS0030
    }
}
