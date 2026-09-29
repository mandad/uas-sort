// src/UasSort.Core/Offload/ProgressMeter.cs
using System.Collections.Immutable;

namespace UasSort.Core.Offload;

/// <summary>Step 12: progress at most every 100 ms while copying, plus once at the end of every job; MB/s over the last 5 s.</summary>
internal sealed class ProgressMeter
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    private readonly TimeProvider _clock;
    private readonly IProgress<OffloadProgress> _sink;
    private readonly int _filesTotal;
    private readonly long _bytesTotal;
    private readonly Queue<(DateTime At, long Bytes)> _samples = new();
    private int _filesDone;
    private long _bytesDone;
    private long _jobStartBytes;
    private CopyJob? _job;
    private CopyPhase _phase;
    private DateTime _last;

    public ProgressMeter(TimeProvider clock, ImmutableArray<CopyJob> jobs, IProgress<OffloadProgress> sink)
    {
        _clock = clock;
        _sink = sink;
        _filesTotal = jobs.Length;
        _bytesTotal = jobs.Sum(j => j.Size);
        _last = Now();
        _samples.Enqueue((_last, 0));
    }

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    public void Begin(CopyJob job)
    {
        _job = job;
        _jobStartBytes = _bytesDone;
        _phase = CopyPhase.CardCheck;
    }

    public void Phase(CopyPhase phase) => _phase = phase;

    public void Bytes(int count)
    {
        _bytesDone += count;
        var now = Now();
        if (now - _last >= Interval) Report(now);
    }

    public void End(CopyJob job)
    {
        _filesDone++;
        _bytesDone = _jobStartBytes + job.Size;
        Report(Now());
    }

    private void Report(DateTime now)
    {
        _last = now;
        _samples.Enqueue((now, _bytesDone));
        while (_samples.Count > 1 && now - _samples.Peek().At > Window) _samples.Dequeue();
        var (at0, bytes0) = _samples.Peek();
        double seconds = (now - at0).TotalSeconds;
        double mbps = seconds > 0 ? (_bytesDone - bytes0) / seconds / 1e6 : 0;
        TimeSpan? eta = mbps > 0 ? TimeSpan.FromSeconds((_bytesTotal - _bytesDone) / (mbps * 1e6)) : null;
        _sink.Report(new OffloadProgress(_filesDone, _filesTotal, _bytesDone, _bytesTotal, Math.Round(mbps, 1), eta,
            _job is null ? null : OffloadPaths.FileName(_job.CardRelPath), _phase, _job?.Group));
    }
}
