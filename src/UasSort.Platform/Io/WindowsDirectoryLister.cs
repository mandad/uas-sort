using System.IO.Enumeration;

namespace UasSort.Platform.Io;

/// <summary>
/// Listing only (Ref §4.1, §5): explicit options, errors collected per directory, never opens a file. Each directory's
/// attributes are read before a directory handle is opened; a path that is not a directory is ERROR_DIRECTORY (267).
/// Name-surrogate reparse directories (junctions, mount points, symbolic links) are returned but not entered; other
/// reparse directories (OneDrive / cloud-file placeholder folders) are entered. The tag comes from the parent's listing.
/// </summary>
public sealed class WindowsDirectoryLister : IDirectoryLister
{
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
    };

    public ListingResult Enumerate(string root, bool recurse, IReadOnlySet<string> excludeDirNames)
    {
        var exclude = new HashSet<string>(excludeDirNames, StringComparer.OrdinalIgnoreCase);
        var fullRoot = Path.GetFullPath(root);
        var entries = ImmutableArray.CreateBuilder<FsEntry>();
        var errors = ImmutableArray.CreateBuilder<(string Path, int Win32Error)>();
        var pending = new Queue<string>();
        pending.Enqueue(fullRoot);
        while (pending.TryDequeue(out var directory))
        {
            // Attributes first (Ref §4.3): the enumerator opens a handle, and that open succeeds on a file.
            if (Kernel32.TryGetAttributes(directory, out var attrError) is not { } attrs) { errors.Add((directory, attrError)); continue; }
            if ((attrs & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0) { errors.Add((directory, Kernel32.ERROR_DIRECTORY)); continue; }
            using var one = new OneDirectory(directory, fullRoot, exclude);
            while (one.MoveNext())
            {
                var e = one.Current;
                entries.Add(e);
                if (recurse && e.IsDirectory && ShouldEnter(e, errors)) pending.Enqueue(e.FullPath);
            }
            foreach (var code in one.Errors) errors.Add((directory, code));
        }
        return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
    }

    /// <summary>
    /// Descend into a directory unless it is a name-surrogate reparse point (IsReparseTagNameSurrogate: junction,
    /// mount point, symlink) or a reparse point whose tag is unknown. Cloud placeholder tags (0x9000xxxA) are entered.
    /// </summary>
    internal static bool ShouldDescend(uint attributes, uint? reparseTag)
        => (attributes & Kernel32.FILE_ATTRIBUTE_REPARSE_POINT) == 0
           || reparseTag is { } tag && (tag & NameSurrogateBit) == 0;

    /// <summary>The reparse tag of <paramref name="fullPath"/>, read without opening it; null when unreadable.</summary>
    internal static uint? TryReadReparseTag(string fullPath, out int error)
        => Kernel32.TryFindEntry(fullPath, out var data, out error) ? data.Reserved0 : null;

    private const uint NameSurrogateBit = 0x20000000;

    private static bool ShouldEnter(FsEntry e, ImmutableArray<(string Path, int Win32Error)>.Builder errors)
    {
        if ((e.RawAttributes & Kernel32.FILE_ATTRIBUTE_REPARSE_POINT) == 0) return true;
        var tag = TryReadReparseTag(e.FullPath, out var error);
        if (tag is null) errors.Add((e.FullPath, error));   // not entered, and the listing says it is incomplete
        return ShouldDescend(e.RawAttributes, tag);
    }

#pragma warning disable RS0030 // IO layer: the one lister; FileSystemEnumerator opens only directory handles (attributes checked first)
    private sealed class OneDirectory(string directory, string root, HashSet<string> exclude)
        : FileSystemEnumerator<FsEntry>(directory, Options)
#pragma warning restore RS0030
    {
        // A field initializer: assigned before the base constructor, which may already call ContinueOnError.
        private readonly List<int> _errors = [];

        public IReadOnlyList<int> Errors => _errors;

        protected override bool ContinueOnError(int error)
        {
            _errors.Add(error);
            return true;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
            => !(entry.IsDirectory && exclude.Contains(entry.FileName.ToString()));

        protected override FsEntry TransformEntry(ref FileSystemEntry entry)
        {
            var full = entry.ToFullPath();
            return new FsEntry(full, Path.GetRelativePath(root, full), entry.IsDirectory, entry.IsDirectory ? 0 : entry.Length,
                               entry.LastWriteTimeUtc.UtcDateTime, entry.CreationTimeUtc.UtcDateTime,
                               entry.LastAccessTimeUtc.UtcDateTime, (uint)entry.Attributes);
        }
    }
}
