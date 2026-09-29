using System.Text;
using UasSort.Platform.Io;

namespace UasSort.Platform.Stores;

internal static class StoreFiles
{
    private static readonly UTF8Encoding Utf8 = new(false);

#pragma warning disable RS0030 // IO layer: Platform's own stores under %LOCALAPPDATA%\uas-sort, each path cleared by IoGate
    public static string? ReadText(string path, GuardContext ctx)
    {
        if (!Exists(path)) return null;
        IoGate.Require(IoOp.ReadData, path, ctx);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, Utf8);
        return reader.ReadToEnd();
    }

    public static void WriteReplace(string path, string text, GuardContext ctx, string? backupPath)
    {
        var dir = Path.GetDirectoryName(path)!;
        IoGate.Require(IoOp.CreateDir, dir, ctx);
        Directory.CreateDirectory(dir);
        var temp = path + ".tmp";
        Delete(temp, ctx);                                   // a temp left by a crash
        IoGate.Require(IoOp.CreateNew, temp, ctx);
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(Utf8.GetBytes(text));
            stream.Flush(flushToDisk: true);
        }
        IoGate.Require(IoOp.Rename, temp, ctx);
        if (Exists(path))
        {
            IoGate.Require(IoOp.Rename, path, ctx);
            if (backupPath is not null) IoGate.Require(IoOp.CreateNew, backupPath, ctx);
            File.Replace(temp, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            IoGate.Require(IoOp.CreateNew, path, ctx);
            File.Move(temp, path);
        }
    }

    public static string WriteNew(string path, string text, GuardContext ctx)
    {
        var dir = Path.GetDirectoryName(path)!;
        IoGate.Require(IoOp.CreateDir, dir, ctx);
        Directory.CreateDirectory(dir);
        var stem = path[..^".json".Length];
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? path : $"{stem}-{n}.json";
            if (Exists(candidate)) continue;
            IoGate.Require(IoOp.CreateNew, candidate, ctx);
            try
            {
                using var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(Utf8.GetBytes(text));
                stream.Flush(flushToDisk: true);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate)) { }
        }
    }

    public static void Rename(string from, string to, GuardContext ctx)
    {
        IoGate.Require(IoOp.Rename, from, ctx);
        IoGate.Require(IoOp.CreateNew, to, ctx);
        File.Move(from, to);
    }

    public static void Delete(string path, GuardContext ctx)
    {
        if (!Exists(path)) return;
        IoGate.Require(IoOp.Delete, path, ctx);
        File.Delete(path);
    }
#pragma warning restore RS0030

    /// <summary>Attributes only. Null attributes are accepted by the policy only for creates, so every other op checks this first;
    /// a failure other than "not found" counts as existing and lets IoGate refuse it.</summary>
    private static bool Exists(string path)
        => Kernel32.TryGetAttributes(path, out var error) is not null || !Kernel32.IsNotFound(error);
}
