// src/UasSort.Core/Ledger/LedgerSnapshotBuilder.cs
using System.Globalization;
using UasSort.Core;

namespace UasSort.Core.Ledger;

/// <summary>Parsed ledger union → <see cref="LedgerSnapshot"/> (Ref §3, §11 "Ledger snapshot and scale").</summary>
public static class LedgerSnapshotBuilder
{
    public static LedgerSnapshot Build(LedgerParseResult parsed, LedgerFolderStatus status)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        var issues = parsed.Issues.ToBuilder();
        var files = new Dictionary<FileKey, LedgerFile>();
        var sets = new Dictionary<(string Name, DateTime First), SortedDictionary<string, long>>();
        var decisions = new List<LedgerDecision>();
        var revoked = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<FileKey, DateTime>();
        var folders = new Dictionary<string, LedgerFolder>(StringComparer.OrdinalIgnoreCase);
        var runs = ImmutableArray.CreateBuilder<LedgerRun>();
        var cardDeletes = ImmutableArray.CreateBuilder<LedgerCardDelete>();

        foreach (ParsedRecord p in parsed.Records)
        {
            string? error;
            switch (p.Record)
            {
                case FileRecord f: error = AddFile(f, files, sets); break;
                case FolderRecord f: error = AddFolder(f, folders); break;
                case SeenRecord s: error = AddSeen(s, seen); break;
                case DecisionRecord d: error = AddDecision(d, decisions); break;
                case RevokeRecord r: error = string.IsNullOrEmpty(r.Decision) ? "missing decision id" : Revoke(r, revoked); break;
                case RunRecord r: error = AddRun(r, runs); break;
                case CardDeleteRecord c: error = AddCardDelete(c, cardDeletes); break;
                case TornRecord: error = null; break;
                default: error = "unknown record kind"; break;
            }
            if (error is not null) issues.Add(new LedgerParseIssue(p.File, p.Line, error));
        }

        var live = new Dictionary<FileKey, LedgerDecision>();
        foreach (LedgerDecision d in decisions)
        {
            if (revoked.Contains(d.Id)) continue;
            if (!live.TryGetValue(d.Key, out LedgerDecision? prev) || d.AtUtc > prev.AtUtc) live[d.Key] = d;
        }

        ImmutableDictionary<string, ImmutableArray<LedgerSet>> setsByName = sets
            .GroupBy(kv => kv.Key.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(
                g => g.Key,
                g => g.OrderBy(kv => kv.Key.First)
                      .Select(kv => new LedgerSet(kv.Key.Name, kv.Key.First,
                                                  [.. kv.Value.Select(m => (Member: m.Key, Size: m.Value))]))
                      .ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);

        return new LedgerSnapshot(files.ToImmutableDictionary(), setsByName, live.ToImmutableDictionary(), seen.ToImmutableDictionary(),
                                  folders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), runs.ToImmutable(), cardDeletes.ToImmutable(),
                                  issues.ToImmutable(), parsed.SourceFiles, status);
    }

    private static string? AddFile(FileRecord f, Dictionary<FileKey, LedgerFile> files,
                                   Dictionary<(string Name, DateTime First), SortedDictionary<string, long>> sets)
    {
        if (string.IsNullOrEmpty(f.Name) || f.Size < 0) return "bad name or size";
        if (f.Src is null || f.Dest is null || f.Run is null) return "missing src, dest or run";
        DestRoot root;
        switch (f.Root)
        {
            case "video": root = DestRoot.Video; break;
            case "photo": root = DestRoot.Photo; break;
            default: return $"bad root '{f.Root}'";
        }
        VerifyKind verify;
        switch (f.Verify)
        {
            case "unbuffered": verify = VerifyKind.Unbuffered; break;
            case "cached": verify = VerifyKind.Cached; break;
            case "nameSize": verify = VerifyKind.NameSize; break;
            default: return $"bad verify '{f.Verify}'";
        }
        UInt128? hash = null;
        if (f.Xxh128 is { Length: > 0 } hex)
        {
            if (!UInt128.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out UInt128 h)) return $"bad xxh128 '{hex}'";
            hash = h;
        }
        GeoPoint? point = f.Lat is { } lat && f.Lon is { } lon ? new GeoPoint(lat, lon) : null;
        SessionKey? session = f.SessionUtc is { } su ? new SessionKey(f.Serial, Utc(su)) : null;
        DateTime? capture = f.CaptureUtc is { } c ? Utc(c) : null;
        var lf = new LedgerFile(FileKey.Of(f.Name, f.Size), f.Src, root, f.Dest, hash, verify, Utc(f.At), capture, point, f.Tz,
                                f.LocalDate, session, f.Set, f.Machine, f.Run);
        if (!files.TryGetValue(lf.Key, out LedgerFile? prev) || Replaces(lf, prev)) files[lf.Key] = lf;

        if (f.Set is { Length: > 0 } set)
        {
            var key = (set, capture ?? DateTime.MinValue);
            if (!sets.TryGetValue(key, out SortedDictionary<string, long>? members))
            {
                members = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                sets[key] = members;
            }
            members[f.Name] = f.Size;
        }
        return null;
    }

    /// <summary>00-interfaces decision 5: the strongest verification per key wins (Unbuffered > Cached > NameSize);
    /// among equal strength the later record wins; a later weaker record never downgrades.</summary>
    private static bool Replaces(LedgerFile candidate, LedgerFile current)
    {
        int byStrength = Strength(candidate.Verify).CompareTo(Strength(current.Verify));
        return byStrength != 0 ? byStrength > 0 : candidate.AtUtc > current.AtUtc;
    }

    private static int Strength(VerifyKind verify) => verify switch
    {
        VerifyKind.Unbuffered => 2,
        VerifyKind.Cached => 1,
        _ => 0,
    };

    private static string? AddFolder(FolderRecord f, Dictionary<string, LedgerFolder> folders)
    {
        if (string.IsNullOrEmpty(f.Path)) return "missing path";
        FolderSource source;
        switch (f.Source)
        {
            case "created": source = FolderSource.Created; break;
            case "appended": source = FolderSource.Appended; break;
            case "cardLeftovers": source = FolderSource.CardLeftovers; break;
            default: return $"bad source '{f.Source}'";
        }
        GeoPoint? centroid = f.Lat is { } lat && f.Lon is { } lon ? new GeoPoint(lat, lon) : null;
        string key = PathRules.Normalize(f.Path);
        var folder = new LedgerFolder(key, f.Desc ?? "", source, centroid, f.Start, f.End, f.Tz);
        if (folders.TryGetValue(key, out LedgerFolder? prev))
        {
            folder = folder with
            {
                Start = prev.Start < folder.Start ? prev.Start : folder.Start,
                End = prev.End > folder.End ? prev.End : folder.End,
                Centroid = folder.Centroid ?? prev.Centroid,
            };
        }
        folders[key] = folder;
        return null;
    }

    private static string? AddSeen(SeenRecord s, Dictionary<FileKey, DateTime> seen)
    {
        if (string.IsNullOrEmpty(s.Name) || s.Size < 0) return "bad name or size";
        FileKey key = FileKey.Of(s.Name, s.Size);
        DateTime at = Utc(s.At);
        if (!seen.TryGetValue(key, out DateTime prev) || at > prev) seen[key] = at;
        return null;
    }

    private static string? AddDecision(DecisionRecord d, List<LedgerDecision> decisions)
    {
        if (string.IsNullOrEmpty(d.Name) || d.Size < 0) return "bad name or size";
        DecisionKind kind;
        switch (d.Kind)
        {
            case "assumedImported": kind = DecisionKind.AssumedImported; break;
            case "dismissed": kind = DecisionKind.Dismissed; break;
            default: return $"bad decision kind '{d.Kind}'";
        }
        decisions.Add(new LedgerDecision(d.Id, FileKey.Of(d.Name, d.Size), kind, Utc(d.At), d.Machine, d.Set, d.Why ?? ""));
        return null;
    }

    private static string? Revoke(RevokeRecord r, HashSet<string> revoked)
    {
        revoked.Add(r.Decision);
        return null;
    }

    private static string? AddRun(RunRecord r, ImmutableArray<LedgerRun>.Builder runs)
    {
        if (r.Card is null || r.Roots is null) return "missing card or roots";
        if (!Enum.TryParse(r.Verdict, ignoreCase: false, out VerdictLevel verdict) || !Enum.IsDefined(verdict)
            || r.Verdict.Any(char.IsDigit)) return $"bad verdict '{r.Verdict}'";
        if (!uint.TryParse(r.Card.Serial, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint serial))
            return $"bad card serial '{r.Card.Serial}'";
        var counts = ImmutableDictionary.CreateBuilder<AuditCategory, int>();
        foreach ((string name, int n) in r.Counts ?? ImmutableDictionary<string, int>.Empty)
        {
            if (Enum.TryParse(name, ignoreCase: false, out AuditCategory cat) && Enum.IsDefined(cat) && !name.Any(char.IsDigit)) counts[cat] = n;
        }
        var card = new CardIdentity(serial, r.Card.Label, r.Card.Fs, 0);   // RunCard carries no capacity (Part 05 gap list)
        runs.Add(new LedgerRun(r.Run, r.Machine, Utc(r.Start), Utc(r.End), r.App, card, r.Card.Model, r.Card.InventoryHash,
                               r.Roots.Video, r.Roots.Photo, verdict, counts.ToImmutable()));
        return null;
    }

    private static string? AddCardDelete(CardDeleteRecord c, ImmutableArray<LedgerCardDelete>.Builder cardDeletes)
    {
        if (string.IsNullOrEmpty(c.Name) || c.Size < 0) return "bad name or size";
        cardDeletes.Add(new LedgerCardDelete(c.Run, Utc(c.At), FileKey.Of(c.Name, c.Size), c.Src, c.Evidence, c.Machine));
        return null;
    }

    private static DateTime Utc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
    };
}
