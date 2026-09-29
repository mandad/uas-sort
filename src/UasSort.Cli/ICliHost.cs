namespace UasSort.Cli;

/// <summary>
/// Everything the CLI needs from Core and Platform. WindowsCliHost is the only implementation that constructs services;
/// none of its members writes anything (no settings, drafts, ledger, reports, logs or files; Ref §4.5).
/// </summary>
internal interface ICliHost
{
    string AppDataDir { get; }
    SettingsLoad LoadSettings(string? settingsPath);
    CardSourceCheck Validate(string cardPath, Settings settings);
    CardIdentity? IdentityFor(string cardRoot);
    Task<ScanResult> ScanAsync(CardSource source, Settings settings, IProgress<ScanProgress> progress, CancellationToken ct);
    Plan Plan(ScanResult scan, Tuning tuning, CancellationToken ct);
    string ReadExpectFile(string path);
}
