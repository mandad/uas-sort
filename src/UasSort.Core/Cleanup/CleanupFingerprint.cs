// src/UasSort.Core/Cleanup/CleanupFingerprint.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Cleanup;

/// <summary>Ref §10.6 Fingerprint: XxHash64 over request, SpaceBefore, the sorted delete lines and NotInLibrary ids (defined here).</summary>
public static class CleanupFingerprint
{
    public static string Compute(CleanupRequest request, CardSpace spaceBefore, IEnumerable<CleanupCandidate> delete)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(spaceBefore);
        ArgumentNullException.ThrowIfNull(delete);
        var inv = CultureInfo.InvariantCulture;
        var units = delete.ToList();
        var sb = new StringBuilder();
        sb.Append(inv, $"{request.Mode}|{request.Before?.ToString("yyyy-MM-dd", inv) ?? "-"}|{request.Goal?.Kind.ToString() ?? "-"}|{request.Goal?.Bytes ?? -1}|{request.IncludeNotInLibrary}\n");
        sb.Append(inv, $"{spaceBefore.FreeBytes}|{spaceBefore.TotalBytes}|{spaceBefore.ClusterBytes}\n");
        foreach (var line in units.SelectMany(c => c.Files)
                                  .Select(f => string.Create(inv, $"{CleanupPaths.Rel(f.RelPath)}|{f.Size}|{f.MtimeUtc.Ticks}"))
                                  .Order(StringComparer.Ordinal))
            sb.Append(line).Append('\n');
        foreach (var id in units.Where(c => c.Eligibility == CleanupEligibility.NotInLibrary)
                                .Select(c => c.Unit.CardRelPath).Order(StringComparer.Ordinal))
            sb.Append(id).Append('\n');
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", inv);
    }
}
