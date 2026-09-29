using System.Text.Json;
using System.Text.RegularExpressions;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

public sealed partial class DraftStore(string appDataDir, string machine, IPathFacts facts) : IDraftStore
{
    private readonly GuardContext _ctx = GuardContexts.ForAppData(appDataDir, machine, facts);
    private readonly string _dir = Path.Join(facts.Canonical(appDataDir), "drafts");

    public Draft? Load(string cardKey)
    {
        var text = StoreFiles.ReadText(PathFor(cardKey), _ctx);
        if (text is null) return null;
        try { return JsonSerializer.Deserialize(text, CoreJsonContext.Default.Draft); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { return null; }   // NotSupported: unknown "t" (as LedgerCodec)
    }

    public void Save(string cardKey, Draft d)
        => StoreFiles.WriteReplace(PathFor(cardKey), JsonSerializer.Serialize(d, CoreJsonContext.Default.Draft), _ctx, backupPath: null);

    public void Delete(string cardKey) => StoreFiles.Delete(PathFor(cardKey), _ctx);

    private string PathFor(string cardKey)
        => KeyPattern().IsMatch(cardKey) ? Path.Join(_dir, cardKey + ".json") : throw new ArgumentException($"Bad draft key {cardKey}", nameof(cardKey));

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex KeyPattern();
}
