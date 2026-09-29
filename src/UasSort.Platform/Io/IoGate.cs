namespace UasSort.Platform.Io;

/// <summary>Every open, create, attribute change, delete and rename in Platform goes through here first (Ref §4.2 Platform row).</summary>
internal static class IoGate
{
    /// <returns>The target's attributes (null = it doesn't exist), when the policy allows the operation.</returns>
    public static uint? Require(IoOp op, string canonicalPath, GuardContext ctx)
    {
        var attributes = PlaceholderGuard.ReadAttributes(canonicalPath);
        return IoGuardPolicy.Check(op, canonicalPath, attributes, ctx) switch
        {
            GuardAllow => attributes,
            GuardCloudOnly c => throw new CloudOnlyFileException(c.Path),
            GuardHydration h => throw new UnsafeIoException(
                $"Refused {op} of {h.Path}: a cloud placeholder (attributes 0x{h.Attributes:X}) would be downloaded"),
            GuardUnsafe u => throw new UnsafeIoException($"Refused {op} of {canonicalPath}: {u.Reason}"),
        };
    }
}
