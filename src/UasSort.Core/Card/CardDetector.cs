// src/UasSort.Core/Card/CardDetector.cs
using System.Text.RegularExpressions;

namespace UasSort.Core.Card;

/// <summary>Flags DJI cards among the ready volumes (Ref §5). Never picks between several cards.</summary>
public static partial class CardDetector
{
    public const string NotACard = "not a DJI card";

    [GeneratedRegex(@"^DJI_\d{3}(_.+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DjiFolder();

    [GeneratedRegex(@"^DJI_(\d{14})_(\d{4})_([A-Z])(?:_[^.]*)?\.([A-Za-z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DjiFileName();

    public static IReadOnlyList<CardCandidate> Detect(IReadOnlyList<VolumeInfo> volumes, IDirectoryLister lister, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(lister);
        ArgumentNullException.ThrowIfNull(settings);
        var result = new List<CardCandidate>();
        foreach (var v in volumes.OrderBy(v => PathRules.Normalize(v.Root), StringComparer.OrdinalIgnoreCase))
        {
            if (!v.IsReady || IsSkippedDriveType(v.DriveType) || HoldsConfiguredRoot(v.Root, settings)) continue;
            var listing = lister.Enumerate(PathRules.Join(v.Root, "DCIM"), recurse: true, ImmutableHashSet<string>.Empty);
            var mediaDirs = listing.Entries
                .Where(e => e.IsDirectory && TopSegment(e.RelPath) == e.RelPath.Replace('/', '\\'))
                .Select(e => PathRules.FileName(e.FullPath))
                .Where(n => DjiFolder().IsMatch(n) || Eq(n, "PANORAMA") || Eq(n, "HYPERLAPSE"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = listing.Entries.Where(e => !e.IsDirectory).ToList();
            var djiNamed = files.Any(e => DjiFileName().IsMatch(PathRules.FileName(e.FullPath)));
            var isDji = mediaDirs.Count > 0 && djiNamed;
            var mediaCount = isDji
                ? files.Count(e => mediaDirs.Contains(TopSegment(e.RelPath)) && CardClassifier.MediaExtensions.Contains(Extension(e.FullPath)))
                : 0;
            result.Add(new CardCandidate(v, isDji, isDji ? null : NotACard, mediaCount));
        }
        return result;
    }

    public static CardCandidate? SingleDjiCard(IReadOnlyList<CardCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var dji = candidates.Where(c => c.IsDjiCard).ToList();
        return dji.Count == 1 ? dji[0] : null;
    }

    private static bool IsSkippedDriveType(string driveType)
        => Eq(driveType, "Network") || Eq(driveType, "CDRom") || Eq(driveType, "NoRootDirectory");

    private static bool HoldsConfiguredRoot(string volumeRoot, Settings s)
        => PathRules.IsSameOrUnder(s.VideoRoot, volumeRoot) || PathRules.IsSameOrUnder(s.PhotoRoot, volumeRoot)
           || s.PreviousPhotoRoots.Any(r => PathRules.IsSameOrUnder(r, volumeRoot));

    private static string TopSegment(string relPath)
    {
        var rel = relPath.Replace('/', '\\');
        var i = rel.IndexOf('\\', StringComparison.Ordinal);
        return i < 0 ? rel : rel[..i];
    }

    private static string Extension(string path)
    {
        var name = PathRules.FileName(path);
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..];
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
