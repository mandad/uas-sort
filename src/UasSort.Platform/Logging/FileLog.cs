using System.Globalization;
using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Logging;

/// <summary>logs\uas-sort-yyyyMMdd.log under %LOCALAPPDATA%\uas-sort, kept 14 days (Ref §11).</summary>
public sealed class FileLog(string appDataDir, string machine, IPathFacts facts, TimeProvider clock)
{
    private const int KeepDays = 14;
    private readonly Lock _gate = new();
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "logs");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}\n{e}");

    public void Prune()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        foreach (var file in new WindowsDirectoryLister().Enumerate(_dir, recurse: false, new HashSet<string>()).Entries.Where(e => !e.IsDirectory))
        {
            var name = Path.GetFileName(file.FullPath);
            if (!name.StartsWith("uas-sort-", StringComparison.Ordinal) || !name.EndsWith(".log", StringComparison.Ordinal)) continue;
            if (!DateOnly.TryParseExact(name["uas-sort-".Length..^".log".Length], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                continue;
            if (today.DayNumber - day.DayNumber < KeepDays) continue;
            IoGate.Require(IoOp.Delete, file.FullPath, _ctx);
#pragma warning disable RS0030 // IO layer: the app's own log files
            File.Delete(file.FullPath);
#pragma warning restore RS0030
        }
    }

    private void Write(string level, string message)
    {
        var now = clock.GetUtcNow();
        var line = $"{now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture)}Z {level} {message}\n";
        var path = Path.Join(_dir, $"uas-sort-{clock.GetLocalNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        lock (_gate)
        {
            // The policy has no append op outside the ledger; under AppDataDir (rule 5) every op is allowed and rule 1 still refuses placeholders.
            IoGate.Require(IoOp.CreateDir, _dir, _ctx);
            IoGate.Require(IoOp.CreateNew, path, _ctx);
#pragma warning disable RS0030 // IO layer: the app's own log files
            Directory.CreateDirectory(_dir);
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
#pragma warning restore RS0030
            stream.Write(Encoding.UTF8.GetBytes(line));
        }
    }
}
