// src/UasSort.Core/Ledger/LedgerCodec.cs
using System.Text.Json;
using UasSort.Core.Json;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>One ledger record ⇄ one JSON line (Ref §11: JSON Lines, "t" discriminator, v:1).</summary>
public static class LedgerCodec
{
    public const int Version = 1;

    public static string Serialize(LedgerRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, LedgerJsonContext.Default.LedgerRecord);
    }

    public static LedgerRecord? TryParse(string line, out string? error)
    {
        ArgumentNullException.ThrowIfNull(line);
        LedgerRecord? record;
        try
        {
            record = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerRecord);
        }
        catch (JsonException ex)
        {
            error = "invalid record: " + ex.Message;
            return null;
        }
        catch (NotSupportedException ex)          // unknown or missing "t" on the abstract base
        {
            error = "invalid record: " + ex.Message;
            return null;
        }
        error = record is null ? "empty record" : Validate(record);
        return error is null ? record : null;
    }

    private static string? Validate(LedgerRecord r)
        => r.V != Version ? $"unsupported v {r.V}"
         : string.IsNullOrWhiteSpace(r.Id) ? "missing id"
         : string.IsNullOrWhiteSpace(r.Machine) ? "missing machine"
         : null;
}
