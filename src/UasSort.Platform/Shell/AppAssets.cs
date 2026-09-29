using System.Reflection;

namespace UasSort.Platform.Shell;

/// <summary>App-folder and embedded assets only (Ref §4.1 IAppAssets).</summary>
public sealed class AppAssets(string appDir, Assembly selfTestAssembly) : IAppAssets
{
    public Stream OpenPlaces()
    {
#pragma warning disable RS0030 // IO layer: the app's own install folder, read-only
        return new FileStream(Path.Join(appDir, "places.bin.gz"), FileMode.Open, FileAccess.Read, FileShare.Read);
#pragma warning restore RS0030
    }

    public Stream OpenSelfTest(string name)
    {
        var resource = selfTestAssembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".SelfTest." + name, StringComparison.Ordinal));
        return (resource is null ? null : selfTestAssembly.GetManifestResourceStream(resource))
               ?? throw new FileNotFoundException($"Embedded self-test asset {name} not found");
    }
}
