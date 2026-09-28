using System.Diagnostics;

namespace UasSpike.App;

/// <summary>Append-only spike log next to the exe (or UAS_SPIKE_LOG).</summary>
public static class SpikeLog
{
    private static readonly Lock Gate = new();
    public static string Path { get; } =
        Environment.GetEnvironmentVariable("UAS_SPIKE_LOG") is { Length: > 0 } p ? p
        : System.IO.Path.Combine(AppContext.BaseDirectory, "spike.log");

    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public static void Write(string line)
    {
        var ms = (DateTime.UtcNow - ProcessStart).TotalMilliseconds;
        lock (Gate)
        {
            File.AppendAllText(Path, $"{DateTime.Now:O} +{ms,7:F0}ms {line}{Environment.NewLine}");
        }
    }
}
