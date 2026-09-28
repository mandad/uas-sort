using UasSort.Testing;

namespace UasSort.Platform.Tests.Build;

/// <summary>Ref §2.4 "guards on the guards": BannedSymbols.txt holds exactly the required entries.</summary>
public sealed class BannedSymbolsTests
{
    private static readonly string[] Required =
    [
        // File-system types
        "T:System.IO.File",
        "T:System.IO.Directory",
        "T:System.IO.FileInfo",
        "T:System.IO.DirectoryInfo",
        "T:System.IO.FileSystemInfo",
        "T:System.IO.RandomAccess",
        "T:System.IO.FileStream",
        "T:System.IO.DriveInfo",
        "T:System.IO.FileSystemWatcher",
        "T:System.IO.Enumeration.FileSystemEnumerable`1",
        "T:System.IO.Enumeration.FileSystemEnumerator`1",
        "T:System.IO.Compression.ZipFile",
        // Path-taking members
        "M:System.IO.Path.GetTempFileName",
        "M:System.IO.StreamReader.#ctor(System.String)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Boolean)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.Int32)",
        "M:System.IO.StreamReader.#ctor(System.String,System.IO.FileStreamOptions)",
        "M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.IO.FileStreamOptions)",
        "M:System.IO.StreamWriter.#ctor(System.String)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding,System.Int32)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.IO.FileStreamOptions)",
        "M:System.IO.StreamWriter.#ctor(System.String,System.Text.Encoding,System.IO.FileStreamOptions)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.IO.FileStream,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)",
        "M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)",
        "M:MetadataExtractor.ImageMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata(System.String,System.Collections.Generic.ICollection{MetadataExtractor.Formats.Jpeg.IJpegSegmentMetadataReader})",
        "M:MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata(System.String)",
        "M:MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata(System.String)",
        "M:System.Xml.Linq.XDocument.Load(System.String)",
        "M:System.Xml.Linq.XDocument.Load(System.String,System.Xml.Linq.LoadOptions)",
        "M:System.Xml.Linq.XDocument.Save(System.String)",
        "M:System.Xml.Linq.XDocument.Save(System.String,System.Xml.Linq.SaveOptions)",
        "M:System.Xml.XmlDocument.Load(System.String)",
        "M:System.Xml.XmlDocument.Save(System.String)",
        // WinRT storage
        "T:Windows.Storage.StorageFile",
        "T:Windows.Storage.StorageFolder",
        "T:Windows.Storage.FileIO",
        "T:Windows.Storage.PathIO",
        "T:Microsoft.VisualBasic.FileIO.FileSystem",
        // Images from paths
        "M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)",
        "P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource",
        // Processes
        "M:System.Diagnostics.Process.Start",
        "M:System.Diagnostics.Process.Start(System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.Collections.Generic.IEnumerable{System.String})",
        "M:System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)",
        "M:System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String)",
        "M:System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String)",
        // Clock
        "P:System.DateTime.Now",
        "P:System.DateTime.Today",
        "P:System.DateTime.UtcNow",
        "P:System.DateTimeOffset.Now",
        "P:System.DateTimeOffset.UtcNow",
    ];

    private static readonly string[] PlatformMustBan =
    [
        "F:System.IO.FileMode.Create",
        "F:System.IO.FileMode.Truncate",
        "F:System.IO.FileMode.OpenOrCreate",
        "F:System.IO.FileMode.Append",
        "M:System.IO.File.Delete(System.String)",
        "M:System.IO.File.Move(System.String,System.String,System.Boolean)",
        "M:System.IO.File.Replace(System.String,System.String,System.String)",
        "M:System.IO.File.SetAttributes(System.String,System.IO.FileAttributes)",
        "M:System.IO.File.SetLastWriteTimeUtc(System.String,System.DateTime)",
        "M:System.IO.File.SetCreationTimeUtc(System.String,System.DateTime)",
        "M:System.IO.Directory.Delete(System.String,System.Boolean)",
        "M:System.IO.Directory.CreateDirectory(System.String)",
        "P:System.DateTime.Now",
        "P:System.DateTime.Today",
        "P:System.DateTime.UtcNow",
        "P:System.DateTimeOffset.Now",
        "P:System.DateTimeOffset.UtcNow",
    ];

    [Fact]
    public void BannedSymbolsTxt_HoldsExactlyTheRefEntries()
    {
        var entries = Entries(RepoPaths.Of("BannedSymbols.txt"));
        Assert.Equal(Required.Length, entries.Count);
        Assert.Equal(Required.Order(StringComparer.Ordinal), entries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void BannedSymbolsTxt_EveryEntryHasAReason()
    {
        foreach (var line in DataLines(RepoPaths.Of("BannedSymbols.txt")))
        {
            var parts = line.Split(';', 2);
            Assert.True(parts.Length == 2 && parts[1].Trim().Length > 0, "no reason for " + line);
        }
    }

    [Fact]
    public void PlatformList_BansWritesDeletesMovesAndTheClock_ButNoWholeTypes()
    {
        var entries = Entries(RepoPaths.Of("src/UasSort.Platform/BannedSymbols.Platform.txt"));
        foreach (var required in PlatformMustBan)
        {
            Assert.Contains(required, entries);
        }

        Assert.DoesNotContain(entries, e => e.StartsWith("T:", StringComparison.Ordinal));
        Assert.DoesNotContain("F:System.IO.FileMode.CreateNew", entries);
    }

    private static List<string> Entries(string path) =>
        DataLines(path).Select(l => l.Split(';', 2)[0].Trim()).ToList();

    private static IEnumerable<string> DataLines(string path) =>
        File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal));
}
