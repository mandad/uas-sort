namespace UasSort.Platform.Io;

/// <summary>IPhotoFileReader (spec 2026-10-04 §3–§4): read-only opens of Picture Offload and Lightroom library files for their EXIF, each
/// after IoGuardPolicy.Check(PhotoCleanupRead), which reads the attributes first and refuses a cloud placeholder (nothing hydrates).
/// FileShare.ReadWrite | Delete: an open read never blocks OneDrive, Lightroom or the recycler.</summary>
public sealed class GuardedPhotoFileReader(Settings settings, string appDataDir, string machine, IPathFacts facts) : IPhotoFileReader
{
    private readonly GuardContext _ctx = GuardContexts.ForPhotoCleanup(settings, appDataDir, machine, facts, plan: null);

    public Stream OpenRead(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        IoGate.Require(IoOp.PhotoCleanupRead, path, _ctx);
#pragma warning disable RS0030 // IO layer: Picture Offload cleanup reads the EXIF of a local photo-root or Lightroom file (guarded, attributes first)
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.RandomAccess, BufferSize = 4096,
        });
#pragma warning restore RS0030
    }
}
