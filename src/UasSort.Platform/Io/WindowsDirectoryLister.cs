using System.IO.Enumeration;

namespace UasSort.Platform.Io;

/// <summary>Listing only (Ref §4.1, §5): explicit options, errors collected per directory, never opens a file.</summary>
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
            using var one = new OneDirectory(directory, fullRoot, exclude);
            while (one.MoveNext())
            {
                var e = one.Current;
                entries.Add(e);
                if (recurse && e.IsDirectory && (e.RawAttributes & (uint)FileAttributes.ReparsePoint) == 0) pending.Enqueue(e.FullPath);
            }
            foreach (var code in one.Errors) errors.Add((directory, code));
        }
        return new ListingResult(entries.ToImmutable(), errors.ToImmutable());
    }

#pragma warning disable RS0030 // IO layer: the one lister; FileSystemEnumerator never opens files, only directory handles
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
