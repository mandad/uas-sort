using System.Globalization;
using System.Text.Json;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

public sealed class ReportStore(string appDataDir, string machine, IPathFacts facts, TimeProvider clock) : IReportStore
{
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "reports");

    public string Save(OffloadReport r)
        => StoreFiles.WriteNew(Name(r.RunId, ""), JsonSerializer.Serialize(r, CoreJsonContext.Default.OffloadReport), _ctx);

    public string Save(CleanupReport r)
        => StoreFiles.WriteNew(Name(r.RunId, "-cleanup"), JsonSerializer.Serialize(r, CoreJsonContext.Default.CleanupReport), _ctx);

    private string Name(string runId, string suffix)
    {
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var run8 = new string(runId.Where(char.IsAsciiLetterOrDigit).Take(8).ToArray());
        return Path.Join(_dir, $"{stamp}-{run8}{suffix}.json");
    }
}
