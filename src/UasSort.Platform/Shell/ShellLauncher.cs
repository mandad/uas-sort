using System.Diagnostics;

namespace UasSort.Platform.Shell;

public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFolder(string path)
    {
        var full = Path.GetFullPath(path);
        if (Kernel32.TryGetAttributes(full, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0)
            throw new DirectoryNotFoundException(full);
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        psi.ArgumentList.Add(full);
        Start(psi);
    }

    public void OpenFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (Kernel32.TryGetAttributes(full, out _) is not uint a || (a & Kernel32.FILE_ATTRIBUTE_DIRECTORY) != 0)
            throw new FileNotFoundException("Nothing to open", full);
        Start(new ProcessStartInfo(full) { UseShellExecute = true });
    }

    public void OpenHttps(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException($"Only https links open: {uri}", nameof(uri));
        Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private static void Start(ProcessStartInfo psi)
    {
#pragma warning disable RS0030 // IO layer: IShellLauncher is the one place that starts processes (Ref §2.4 Processes)
        using var _ = Process.Start(psi);
#pragma warning restore RS0030
    }
}
