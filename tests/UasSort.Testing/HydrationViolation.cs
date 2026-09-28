using System.Globalization;

namespace UasSort.Testing;

/// <summary>The fake file system's hydration tripwire (Ref §4.3): a placeholder, cloud-only ledger file or pre-existing
/// library file was opened.</summary>
public sealed class HydrationViolation : UnsafeIoException
{
    public HydrationViolation() : base("Hydration tripwire") { }
    public HydrationViolation(string message) : base(message) { }
    public HydrationViolation(string message, Exception inner) : base(message, inner) { }

    public HydrationViolation(string path, uint attributes)
        : base(string.Create(CultureInfo.InvariantCulture, $"Hydration tripwire: {path} (attributes 0x{attributes:X})"))
    {
        Path = path;
        Attributes = attributes;
    }

    public string Path { get; } = "";
    public uint Attributes { get; }
}
