namespace UasSort.Core;

public readonly record struct ItemId(string CardRelPath);      // "DCIM/DJI_001/DJI_20260927140627_0128_D.MP4"; a set = "DCIM/PANORAMA/001_0087"

public readonly record struct GeoPoint(double Lat, double Lon); // WGS84 degrees; JSON as [lon,lat] (Task 02.5)

public readonly record struct Distance(double Meters)          // Earth radius 6,371,008.8 m, shared with map.js
{
    public const double EarthRadiusMeters = 6_371_008.8;
    public double Miles => Meters / 1609.344;
    public static Distance FromMiles(double mi) => new(mi * 1609.344);
}

public readonly record struct ByteRange(long Offset, int Length);

public enum ItemKind { Video, Photo, Set }

public enum SetKind { Panorama, Hyperlapse }

public readonly record struct FileKey(string NormName, long Size)
{
    /// <summary>Ref §7.1: the lowercase name with any trailing " (n)" before the extension removed.</summary>
    public static string NormalizeName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var dot = fileName.LastIndexOf('.');
        var stem = dot < 0 ? fileName : fileName[..dot];
        var ext = dot < 0 ? "" : fileName[dot..];
        if (stem.EndsWith(')'))
        {
            var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0)
            {
                var digits = stem[(open + 2)..^1];
                if (digits.Length > 0 && digits.All(char.IsAsciiDigit)) stem = stem[..open];
            }
        }
#pragma warning disable CA1308 // Ref §7.1: keys are lowercase by definition
        return (stem + ext).ToLowerInvariant();
#pragma warning restore CA1308
    }

    public static FileKey Of(string fileName, long size) => new(NormalizeName(fileName), size);

    /// <summary>The key of the last segment of a card-relative ('/') or Windows ('\') path, or of a bare file name.</summary>
    public static FileKey OfPath(string pathOrName, long size)
    {
        ArgumentNullException.ThrowIfNull(pathOrName);
        var cut = Math.Max(pathOrName.LastIndexOf('/'), pathOrName.LastIndexOf('\\'));
        return Of(cut < 0 ? pathOrName : pathOrName[(cut + 1)..], size);
    }
}
