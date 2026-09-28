// src/UasSort.Core/Card/CardClassifier.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace UasSort.Core.Card;

/// <summary>Classifies every card file with the fail-safe rules of Ref §5: only named rules skip; unclaimed media is Unknown.</summary>
public sealed partial class CardClassifier(TimeProvider clock)
{
    public static readonly ImmutableHashSet<string> MediaExtensions = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "mp4", "mov", "dng", "jpg", "jpeg", "heic", "heif", "tif", "tiff", "insv", "avi");

    private const string DjiNoBackup = "DJI: no backup needed";
    private const string SystemRule = "system";
    private const string Recovery = "system/recovery";
    private const string OutsideDcim = "outside DCIM, not media";

    [GeneratedRegex(@"^DJI_\d{3}(_.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MediaFolder();

    [GeneratedRegex(@"^MISC/(FC\d+)\.db$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelDatabase();

    public CardInventory Classify(CardSource source, ListingResult listing)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listing);

        var files = listing.Entries.Where(e => !e.IsDirectory)
            .Select(e => (Rel: e.RelPath.Replace('\\', '/').TrimStart('/'), Entry: e))
            .OrderBy(x => x.Rel, StringComparer.Ordinal)
            .ToList();

        var dngStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mp4Stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trinfTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, _) in files)
        {
            var (folder, name) = SplitFolder(rel);
            var ext = Extension(name);
            if (ext == "dng") dngStems.Add(folder + "/" + Stem(name));
            if (ext == "mp4") mp4Stems.Add(folder + "/" + Stem(name));
            if (name.StartsWith('.') && ext == "trinf") trinfTargets.Add(folder + "/" + name[1..^".trinf".Length]);
        }

        var entries = ImmutableArray.CreateBuilder<CardEntry>(files.Count);
        foreach (var (rel, e) in files)
        {
            var (cls, rule) = ClassifyOne(rel, dngStems, mp4Stems);
            entries.Add(new CardEntry(rel, e.Size, e.MtimeUtc, e.CreationUtc, e.LastAccessUtc, e.RawAttributes, cls, rule));
        }
        var all = entries.ToImmutable();

        var model = all.Select(x => ModelDatabase().Match(x.RelPath)).FirstOrDefault(m => m.Success)?.Groups[1].Value
                       .ToUpperInvariant();

        var warnings = listing.Errors.Select(err => new ScanWarning("EnumerationError",
            string.Create(CultureInfo.InvariantCulture, $"Couldn't read {err.Path} (Win32 error {err.Win32Error})"),
            RelativeOrNull(err.Path, source.Root), ForcesNotSafe: true)).ToImmutableArray();

        return new CardInventory(source, clock.GetUtcNow().UtcDateTime, ComputeInventoryHash(all), all,
                                 BuildUnits(all, trinfTargets), model, warnings);
    }

    public static string ComputeInventoryHash(IEnumerable<CardEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sb = new StringBuilder();
        foreach (var e in files.OrderBy(e => Lower(e.RelPath), StringComparer.Ordinal))
            sb.Append(e.RelPath).Append('|')
              .Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(e.MtimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", CultureInfo.InvariantCulture);
    }

    private static (EntryClass Class, string? Rule) ClassifyOne(string rel, HashSet<string> dngStems, HashSet<string> mp4Stems)
    {
        var seg = rel.Split('/');
        var name = seg[^1];
        var ext = Extension(name);
        var isMedia = MediaExtensions.Contains(ext);
        var underDcim = seg.Length > 1 && Eq(seg[0], "DCIM");

        if (seg.Length > 1 && (Eq(seg[0], "MISC") || Eq(seg[0], "LOST.DIR"))) return (EntryClass.Skip, DjiNoBackup);
        if (seg.Length > 1 && (Eq(seg[0], "Android") || Eq(seg[0], "System Volume Information") || Eq(seg[0], "$RECYCLE.BIN")))
            return (EntryClass.Skip, SystemRule);
        for (var i = 0; i < seg.Length - 1; i++)
            if (Eq(seg[i], ".Trashes") || Eq(seg[i], ".Spotlight-V100") || Eq(seg[i], ".fseventsd")) return (EntryClass.Skip, Recovery);
        if (ext == "lrf") return (EntryClass.Skip, "proxy");
        if (ext == "srt") return (EntryClass.Skip, "captions");
        if (name.StartsWith("._", StringComparison.Ordinal) || (name.StartsWith('.') && ext is "trinf" or "avc1"))
            return (EntryClass.Skip, Recovery);
        if (name.StartsWith('.')) return isMedia || underDcim ? (EntryClass.Unknown, null) : (EntryClass.Skip, OutsideDcim);

        if (seg.Length == 3 && underDcim && MediaFolder().IsMatch(seg[1]))
        {
            var key = seg[0] + "/" + seg[1] + "/" + Stem(name);
            return ext switch
            {
                "mp4" => (EntryClass.Video, null),
                "dng" => (EntryClass.Photo, null),
                "jpg" when dngStems.Contains(key) => (EntryClass.PhotoTwin, null),
                "jpg" when mp4Stems.Contains(key) => (EntryClass.Unknown, "possible video cover"),
                "jpg" => (EntryClass.Photo, null),
                _ => (EntryClass.Unknown, null),
            };
        }
        if (seg.Length == 4 && underDcim && (Eq(seg[1], "PANORAMA") || Eq(seg[1], "HYPERLAPSE")) && ext is "dng" or "jpg")
            return (EntryClass.SetMember, null);
        return isMedia || underDcim ? (EntryClass.Unknown, null) : (EntryClass.Skip, OutsideDcim);
    }

    private static ImmutableArray<MediaUnit> BuildUnits(ImmutableArray<CardEntry> all, HashSet<string> trinfTargets)
    {
        var units = new List<MediaUnit>();
        var twins = new Dictionary<string, CardEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in all.Where(e => e.Class == EntryClass.PhotoTwin)) twins.TryAdd(StemKey(t.RelPath), t);
        foreach (var e in all)
        {
            if (e.Class == EntryClass.Video)
                units.Add(new VideoUnit(new ItemId(e.RelPath), e, trinfTargets.Contains(e.RelPath)));
            else if (e.Class == EntryClass.Photo)
                units.Add(new PhotoUnit(new ItemId(e.RelPath), e,
                    Extension(e.RelPath) == "dng" ? twins.GetValueOrDefault(StemKey(e.RelPath)) : null));
        }
        foreach (var set in all.Where(e => e.Class == EntryClass.SetMember)
                               .GroupBy(e => string.Join('/', e.RelPath.Split('/')[..3]), StringComparer.OrdinalIgnoreCase))
        {
            var seg = set.Key.Split('/');
            var kind = Eq(seg[1], "PANORAMA") ? SetKind.Panorama : SetKind.Hyperlapse;
            units.Add(new SetUnit(new ItemId(set.Key), kind, seg[2], [.. set.OrderBy(m => m.RelPath, StringComparer.Ordinal)]));
        }
        return [.. units.OrderBy(u => u.Id.CardRelPath, StringComparer.Ordinal)];
    }

    private static (string Folder, string Name) SplitFolder(string rel)
    {
        var i = rel.LastIndexOf('/');
        return i < 0 ? ("", rel) : (rel[..i], rel[(i + 1)..]);
    }

    private static string StemKey(string rel)
    {
        var (folder, name) = SplitFolder(rel);
        return folder + "/" + Stem(name);
    }

    private static string Stem(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    private static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? "" : Lower(name[(dot + 1)..]);
    }

    private static string? RelativeOrNull(string path, string root)
        => PathRules.IsStrictlyUnder(path, root) ? PathRules.RelativeCardPath(path, root) : null;

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

#pragma warning disable CA1308 // extensions and the inventory-hash sort key are lowercase by definition (Ref §4.2)
    private static string Lower(string s) => s.ToLowerInvariant();
#pragma warning restore CA1308
}
