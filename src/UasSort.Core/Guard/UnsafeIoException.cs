namespace UasSort.Core;

/// <summary>An IO operation the guard refused (Ref §4.3). Deliberately NOT an IOException: IO error handling must never
/// swallow a safety stop. The run stops with InternalSafetyStop and the event is logged as a bug.</summary>
public class UnsafeIoException : Exception
{
    public UnsafeIoException() : base("Unsafe IO refused by the guard") { }
    public UnsafeIoException(string message) : base(message) { }
    public UnsafeIoException(string message, Exception inner) : base(message, inner) { }
}
