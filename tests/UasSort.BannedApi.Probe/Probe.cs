using System.Diagnostics;
using System.IO.Compression;
using System.IO.Enumeration;
using System.IO.MemoryMappedFiles;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UasSort.BannedApi.Probe;

// One call per BannedSymbols.txt entry (Ref §2.4). Each probe line ends with "// probe: <documentation id>";
// tools/build.ps1 -CheckBannedApi requires exactly one RS0030 on each such line and none anywhere else.
// Never executed: nothing references this project and it is not in uas-sort.slnx.
internal static class Probe
{
    internal static void FileSystemTypes()
    {
        _ = File.Exists("x"); // probe: T:System.IO.File
        _ = Directory.Exists("x"); // probe: T:System.IO.Directory
        _ = new FileInfo("x"); // probe: T:System.IO.FileInfo
        _ = new DirectoryInfo("x"); // probe: T:System.IO.DirectoryInfo
        _ = typeof(FileSystemInfo); // probe: T:System.IO.FileSystemInfo
        _ = RandomAccess.GetLength(null!); // probe: T:System.IO.RandomAccess
        _ = new FileStream("x", FileMode.Open); // probe: T:System.IO.FileStream
        _ = new DriveInfo("C"); // probe: T:System.IO.DriveInfo
        _ = new FileSystemWatcher(); // probe: T:System.IO.FileSystemWatcher
        _ = typeof(FileSystemEnumerable<string>); // probe: T:System.IO.Enumeration.FileSystemEnumerable`1
        _ = typeof(FileSystemEnumerator<string>); // probe: T:System.IO.Enumeration.FileSystemEnumerator`1
        _ = ZipFile.OpenRead("x.zip"); // probe: T:System.IO.Compression.ZipFile
    }

    internal static void PathTakingMembers()
    {
        _ = Path.GetTempFileName(); // probe: M:System.IO.Path.GetTempFileName
        _ = new StreamReader("x"); // probe: M:System.IO.StreamReader.#ctor(System.String)
        _ = new StreamReader("x", true); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Boolean)
        _ = new StreamReader("x", Encoding.UTF8); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding)
        _ = new StreamReader("x", Encoding.UTF8, true); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean)
        _ = new StreamReader("x", Encoding.UTF8, true, 4096); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.Int32)
        _ = new StreamReader("x", new FileStreamOptions()); // probe: M:System.IO.StreamReader.#ctor(System.String,System.IO.FileStreamOptions)
        _ = new StreamReader("x", Encoding.UTF8, true, new FileStreamOptions()); // probe: M:System.IO.StreamReader.#ctor(System.String,System.Text.Encoding,System.Boolean,System.IO.FileStreamOptions)
        _ = new StreamWriter("x"); // probe: M:System.IO.StreamWriter.#ctor(System.String)
        _ = new StreamWriter("x", true); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean)
        _ = new StreamWriter("x", true, Encoding.UTF8); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding)
        _ = new StreamWriter("x", true, Encoding.UTF8, 4096); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Boolean,System.Text.Encoding,System.Int32)
        _ = new StreamWriter("x", new FileStreamOptions()); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.IO.FileStreamOptions)
        _ = new StreamWriter("x", Encoding.UTF8, new FileStreamOptions()); // probe: M:System.IO.StreamWriter.#ctor(System.String,System.Text.Encoding,System.IO.FileStreamOptions)
        _ = MemoryMappedFile.CreateFromFile("x"); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m"); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m", 0); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64)
        _ = MemoryMappedFile.CreateFromFile("x", FileMode.Open, "m", 0, MemoryMappedFileAccess.Read); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.String,System.IO.FileMode,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess)
        _ = MemoryMappedFile.CreateFromFile(fileStream: null!, mapName: null, capacity: 0, access: MemoryMappedFileAccess.Read, inheritability: HandleInheritability.None, leaveOpen: false); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(System.IO.FileStream,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)
        _ = MemoryMappedFile.CreateFromFile(fileHandle: null!, mapName: null, capacity: 0, access: MemoryMappedFileAccess.Read, inheritability: HandleInheritability.None, leaveOpen: false); // probe: M:System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle,System.String,System.Int64,System.IO.MemoryMappedFiles.MemoryMappedFileAccess,System.IO.HandleInheritability,System.Boolean)
        _ = MetadataExtractor.ImageMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.ImageMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Avi.AviMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Bmp.BmpMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Eps.EpsMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Gif.GifMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Ico.IcoMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Jpeg.JpegMetadataReader.ReadMetadata(System.String,System.Collections.Generic.ICollection{MetadataExtractor.Formats.Jpeg.IJpegSegmentMetadataReader})
        _ = MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Netpbm.NetpbmMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Pcx.PcxMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Photoshop.PsdMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Png.PngMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Tga.TgaMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Tiff.TiffMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.Wav.WavMetadataReader.ReadMetadata(System.String)
        _ = MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata("x"); // probe: M:MetadataExtractor.Formats.WebP.WebPMetadataReader.ReadMetadata(System.String)
        _ = XDocument.Load("x"); // probe: M:System.Xml.Linq.XDocument.Load(System.String)
        _ = XDocument.Load("x", LoadOptions.None); // probe: M:System.Xml.Linq.XDocument.Load(System.String,System.Xml.Linq.LoadOptions)
        new XDocument().Save("x"); // probe: M:System.Xml.Linq.XDocument.Save(System.String)
        new XDocument().Save("x", SaveOptions.None); // probe: M:System.Xml.Linq.XDocument.Save(System.String,System.Xml.Linq.SaveOptions)
        new XmlDocument().Load("x"); // probe: M:System.Xml.XmlDocument.Load(System.String)
        new XmlDocument().Save("x"); // probe: M:System.Xml.XmlDocument.Save(System.String)
    }

    internal static void WinRtStorageAndImages()
    {
        _ = Windows.Storage.StorageFile.GetFileFromPathAsync("x"); // probe: T:Windows.Storage.StorageFile
        _ = Windows.Storage.StorageFolder.GetFolderFromPathAsync("x"); // probe: T:Windows.Storage.StorageFolder
        _ = Windows.Storage.FileIO.ReadTextAsync(null!); // probe: T:Windows.Storage.FileIO
        _ = Windows.Storage.PathIO.ReadTextAsync("x"); // probe: T:Windows.Storage.PathIO
        _ = Microsoft.VisualBasic.FileIO.FileSystem.FileExists("x"); // probe: T:Microsoft.VisualBasic.FileIO.FileSystem
        _ = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///x.png")); // probe: M:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.#ctor(System.Uri)
        _ = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage().UriSource; // probe: P:Microsoft.UI.Xaml.Media.Imaging.BitmapImage.UriSource
    }

    internal static void ProcessesAndClock()
    {
        _ = new Process().Start(); // probe: M:System.Diagnostics.Process.Start
        _ = Process.Start("x"); // probe: M:System.Diagnostics.Process.Start(System.String)
        _ = Process.Start("x", "y"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String)
        _ = Process.Start("x", new List<string> { "y" }); // probe: M:System.Diagnostics.Process.Start(System.String,System.Collections.Generic.IEnumerable{System.String})
        _ = Process.Start(new ProcessStartInfo("x")); // probe: M:System.Diagnostics.Process.Start(System.Diagnostics.ProcessStartInfo)
        _ = Process.Start("x", "u", new SecureString(), "d"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String,System.Security.SecureString,System.String)
        _ = Process.Start("x", "a", "u", new SecureString(), "d"); // probe: M:System.Diagnostics.Process.Start(System.String,System.String,System.String,System.Security.SecureString,System.String)
        _ = DateTime.Now; // probe: P:System.DateTime.Now
        _ = DateTime.Today; // probe: P:System.DateTime.Today
        _ = DateTime.UtcNow; // probe: P:System.DateTime.UtcNow
        _ = DateTimeOffset.Now; // probe: P:System.DateTimeOffset.Now
        _ = DateTimeOffset.UtcNow; // probe: P:System.DateTimeOffset.UtcNow
    }
}
