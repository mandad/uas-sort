// src/UasSort.Core/Cleanup/Photos/PhotoCleanupFingerprint.cs
using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace UasSort.Core.Cleanup;

/// <summary>XxHash64 over the request and every eligible row (key, kind, verification, members' names, sizes and mtimes).</summary>
public static class PhotoCleanupFingerprint
{
    public static string Compute(string photoRoot, PhotoCleanupRequest request, IEnumerable<PhotoRow> rows)
    {
        ArgumentNullException.ThrowIfNull(photoRoot);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(inv, $"{PathRules.Normalize(photoRoot).ToUpperInvariant()}|{request.Mode}|{request.Cutoff:yyyy-MM-dd}|{request.LightroomFolder ?? "-"}\n");
        foreach (var r in rows.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(inv, $"{r.Key.ToUpperInvariant()}|{r.Item.Kind}|{r.Eligibility}|{r.Verification.Verified}|{r.Verification.Evidence}\n");
            foreach (var m in r.Item.Members.OrderBy(m => m.RelPath, StringComparer.OrdinalIgnoreCase))
                sb.Append(inv, $"  {m.RelPath.ToUpperInvariant()}|{m.Size}|{m.MtimeUtc.Ticks}\n");
        }
        return XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(sb.ToString())).ToString("x16", inv);
    }
}
