namespace UasSort.Platform.Io;

/// <summary>Ref §4.3 Placeholders: attributes are read first (no open); unreadable attributes refuse the open.
/// Process start exposes placeholders through Part 01's PlaceholderMode.ExposePlaceholders(), not here.</summary>
public static class PlaceholderGuard
{
    public static uint? ReadAttributes(string fullPath)
    {
        var attributes = Kernel32.TryGetAttributes(fullPath, out var error);
        if (attributes is not null) return attributes;
        if (Kernel32.IsNotFound(error)) return null;
        throw new UnsafeIoException($"The attributes of {fullPath} can't be read (Win32 error {error}); refusing to open it");
    }
}
