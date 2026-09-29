using System.Globalization;
using System.Text;

namespace UasSort.Platform.Io;

/// <summary>
/// Reads a small user-named text file (the CLI's --expect file) without ever writing, and without hydrating a
/// cloud-only placeholder: the attributes are read before any open (the PlaceholderGuard rule of Ref §4.3).
/// </summary>
public static class ReadOnlyTextFile
{
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    public static string Read(string path, int maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
#pragma warning disable RS0030 // IO layer: read-only open of a user-named JSON file (CLI --expect); attributes checked first
        FileAttributes attributes = File.GetAttributes(full);   // FileNotFoundException when missing
        if ((attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0)
            throw new IOException($"'{full}' is a cloud-only placeholder; make it available on this device first");
        if ((attributes & FileAttributes.Directory) != 0)
            throw new IOException($"'{full}' is a folder, not a file");
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                                          bufferSize: 4096, FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"'{full}' is larger than {maxBytes} bytes"));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
#pragma warning restore RS0030
    }
}
