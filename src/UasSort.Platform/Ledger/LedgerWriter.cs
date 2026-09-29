using System.Runtime.InteropServices;
using System.Text;

namespace UasSort.Platform.Ledger;

/// <summary>ILedgerWriter over the own file (FileShare.Read: the single writer) and its local mirror (Ref §4.1, §11).
/// Every line is LedgerCodec.Serialize output (decision 37).</summary>
internal sealed class LedgerWriter : ILedgerWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly FileStream _own;
    private readonly FileStream _mirror;

    /// <summary>Takes ownership of both streams; repairs each torn tail with its own torn record.</summary>
    public LedgerWriter(FileStream own, FileStream mirror, string machine, TimeProvider clock)
    {
        _own = own;
        _mirror = mirror;
        RepairTail(_own, machine, clock);
        RepairTail(_mirror, machine, clock);
    }

    public void Append(LedgerRecord r) => AppendRawLine(LedgerCodec.Serialize(r));

    internal void AppendRawLine(string jsonLine)
    {
        var bytes = Utf8.GetBytes(jsonLine + "\n");
        WriteFlushed(_own, bytes);
        WriteFlushed(_mirror, bytes);
    }

    public void Dispose()
    {
        _own.Dispose();
        _mirror.Dispose();
    }

    private static void RepairTail(FileStream s, string machine, TimeProvider clock)
    {
        var (length, newlines, lastByte) = Scan(s);
        s.Seek(0, SeekOrigin.End);
        if (length == 0 || lastByte == (byte)'\n') return;
        var torn = new TornRecord(LedgerCodec.Version, Guid.NewGuid().ToString("N"), machine, clock.GetUtcNow().UtcDateTime, newlines + 1);
        WriteFlushed(s, Utf8.GetBytes("\n" + LedgerCodec.Serialize(torn) + "\n"));
    }

    private static (long Length, int Newlines, byte LastByte) Scan(FileStream s)
    {
        s.Seek(0, SeekOrigin.Begin);
        var buffer = new byte[1 << 16];
        var newlines = 0;
        byte last = 0;
        long length = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            newlines += buffer.AsSpan(0, n).Count((byte)'\n');
            last = buffer[n - 1];
            length += n;
        }
        return (length, newlines, last);
    }

    private static void WriteFlushed(FileStream s, byte[] bytes)
    {
        s.Write(bytes);
        s.Flush();
        if (!Kernel32.FlushFileBuffers(s.SafeFileHandle))
            throw new IOException($"FlushFileBuffers({s.Name}) failed: {Marshal.GetLastPInvokeError()}");
    }
}
