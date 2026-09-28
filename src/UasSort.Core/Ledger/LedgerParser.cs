// src/UasSort.Core/Ledger/LedgerParser.cs
using UasSort.Core;

namespace UasSort.Core.Ledger;

public sealed record LedgerFileText(string FullPath, string Text);
public sealed record ParsedRecord(string File, int Line, LedgerRecord Record);
public sealed record LedgerParseResult(ImmutableArray<ParsedRecord> Records, ImmutableArray<LedgerParseIssue> Issues,
                                       ImmutableArray<string> SourceFiles);

/// <summary>The union of every ledger*.jsonl (Ref §11): dedupe by id, torn lines skipped, other bad lines reported.</summary>
public static class LedgerParser
{
    public static LedgerParseResult Parse(IReadOnlyList<LedgerFileText> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var records = ImmutableArray.CreateBuilder<ParsedRecord>();
        var issues = ImmutableArray.CreateBuilder<LedgerParseIssue>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        List<LedgerFileText> ordered = [.. sources.OrderBy(s => s.FullPath, StringComparer.OrdinalIgnoreCase)];

        foreach (LedgerFileText source in ordered)
        {
            string[] lines = source.Text.Split('\n');
            bool terminated = source.Text.Length == 0 || source.Text[^1] == '\n';
            int count = terminated ? lines.Length - 1 : lines.Length;   // after a final '\n', Split yields one empty tail
            var parsed = new LedgerRecord?[count];
            var errors = new string?[count];
            for (int i = 0; i < count; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line)) continue;
                parsed[i] = LedgerCodec.TryParse(line, out errors[i]);
            }

            var tornLines = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (parsed[i] is TornRecord t && t.Line >= 1 && t.Line < i + 1) tornLines.Add(t.Line);
            }

            for (int i = 0; i < count; i++)
            {
                int lineNo = i + 1;
                if (tornLines.Contains(lineNo)) continue;             // named by a later torn marker of this file
                if (!terminated && i == count - 1) continue;          // torn final line: never an issue
                if (errors[i] is { } error)
                {
                    issues.Add(new LedgerParseIssue(source.FullPath, lineNo, error));
                    continue;
                }
                if (parsed[i] is not { } record) continue;           // blank line
                if (ids.Add(record.Id)) records.Add(new ParsedRecord(source.FullPath, lineNo, record));
            }
        }

        return new LedgerParseResult(records.ToImmutable(), issues.ToImmutable(), [.. ordered.Select(s => s.FullPath)]);
    }
}
