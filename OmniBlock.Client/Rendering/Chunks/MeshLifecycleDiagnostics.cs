using System.Diagnostics;
using System.Globalization;
using System.Text;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

internal enum MeshLifecycleStage
{
    Invalidated, Deferred, Queued, Snapshotting, WorkerQueued, Building,
    AwaitingUpload, Uploaded, DrawRecorded, EmptyReady, Cancelled, Evicted
}

internal enum MeshCancellationReason
{
    None, Superseded, OutsideRetention, RendererDisposed, Orphaned,
    DuplicateRequest, BuildFailed, SnapshotFailed, SectionReplaced
}

internal readonly record struct MeshLifecycleEvent(
    long Sequence, long Timestamp, long SectionId, long RequestId, Vector3D<int> Position,
    long Epoch, MeshWorkPriority Priority, SectionDirtyReason DirtyReasons,
    MeshLifecycleStage Stage, MeshCancellationReason Reason);

/// <summary>
///     A diagnostic identity, never a scheduling/cancellation token. Workers hold this rather than
///     a mutable SectionRenderState (whose pooled version may already belong to another section).
///     All mutable fields below are protected by the owning recorder's lock.
/// </summary>
internal sealed class MeshLifecycleRequest(
    long id, long sectionId, Vector3D<int> position, long epoch,
    MeshWorkPriority priority, SectionDirtyReason reasons)
{
    internal readonly long Id = id;
    internal readonly long SectionId = sectionId;
    internal readonly Vector3D<int> Position = position;
    internal readonly long Epoch = epoch;
    internal MeshWorkPriority Priority = priority;
    internal readonly SectionDirtyReason Reasons = reasons;
    internal MeshLifecycleStage Stage = MeshLifecycleStage.Queued;
    internal readonly long QueuedAt = Stopwatch.GetTimestamp();
    internal long StageAt = Stopwatch.GetTimestamp();
    internal bool Terminal;
}

internal readonly record struct MeshLifecycleSnapshot(
    long Events, long OverwrittenEvents, long Cancelled, long Superseded, long BuildFailures,
    long Queued, long Snapshotting, long WorkerQueued, long Building, long AwaitingUpload,
    long AwaitingDraw, long DrawRecorded, long EmptyReady);

/// <summary>
///     Bounded cross-thread event history. The stack profiler cannot correlate a dirty notification
///     with a later worker and render frame. Record only lifecycle transitions, never vertices or
///     every draw; no file I/O or string formatting occurs on worker threads. Cumulative counters
///     survive ring rollover. Cancellation means the request will not be installed/presented, NOT
///     that a running worker was preempted. DrawRecorded is CPU command recording, not GPU completion.
/// </summary>
internal sealed class MeshLifecycleDiagnostics(int capacity = 8192)
{
    private readonly object _gate = new();
    private readonly MeshLifecycleEvent[] _events = new MeshLifecycleEvent[
        capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private readonly long[] _active = new long[Enum.GetValues<MeshLifecycleStage>().Length];
    private readonly long _origin = Stopwatch.GetTimestamp();
    private long _sequence;
    private long _requestId;
    private long _cancelled, _superseded, _buildFailures, _drawRecorded, _emptyReady;

    public MeshLifecycleRequest Queue(long sectionId, Vector3D<int> position, long epoch,
        MeshWorkPriority priority, SectionDirtyReason reasons)
    {
        lock (_gate)
        {
            var request = new MeshLifecycleRequest(++_requestId, sectionId, position, epoch, priority, reasons);
            _active[(int)MeshLifecycleStage.Queued]++;
            Append(request, MeshLifecycleStage.Queued, MeshCancellationReason.None);
            return request;
        }
    }

    public void Move(MeshLifecycleRequest? request, MeshLifecycleStage stage,
        MeshWorkPriority? priority = null)
    {
        if (request == null) return;
        lock (_gate)
        {
            if (request.Terminal || stage <= request.Stage) return;
            _active[(int)request.Stage]--;
            request.Stage = stage;
            request.StageAt = Stopwatch.GetTimestamp();
            if (priority.HasValue) request.Priority = priority.Value;
            request.Terminal = stage is MeshLifecycleStage.DrawRecorded or MeshLifecycleStage.EmptyReady;
            if (!request.Terminal) _active[(int)stage]++;
            if (stage == MeshLifecycleStage.DrawRecorded) _drawRecorded++;
            if (stage == MeshLifecycleStage.EmptyReady) _emptyReady++;
            Append(request, stage, MeshCancellationReason.None);
        }
    }

    public void Cancel(MeshLifecycleRequest? request, MeshCancellationReason reason)
    {
        if (request == null) return;
        if (reason == MeshCancellationReason.None) throw new ArgumentException("A cancellation needs a reason.", nameof(reason));
        lock (_gate)
        {
            if (request.Terminal) return;
            _active[(int)request.Stage]--;
            request.Terminal = true;
            request.Stage = MeshLifecycleStage.Cancelled;
            request.StageAt = Stopwatch.GetTimestamp();
            _cancelled++;
            if (reason == MeshCancellationReason.Superseded) _superseded++;
            if (reason is MeshCancellationReason.BuildFailed or MeshCancellationReason.SnapshotFailed) _buildFailures++;
            Append(request, MeshLifecycleStage.Cancelled, reason);
        }
    }

    public void Note(long sectionId, Vector3D<int> position, long epoch, MeshWorkPriority priority,
        SectionDirtyReason reasons, MeshLifecycleStage stage, MeshCancellationReason reason = MeshCancellationReason.None)
    {
        lock (_gate)
            Append(new MeshLifecycleEvent(0, 0, sectionId, 0, position, epoch, priority, reasons, stage, reason));
    }

    private void Append(MeshLifecycleRequest request, MeshLifecycleStage stage, MeshCancellationReason reason) =>
        Append(new MeshLifecycleEvent(0, 0, request.SectionId, request.Id, request.Position,
            request.Epoch, request.Priority, request.Reasons, stage, reason));

    private void Append(MeshLifecycleEvent entry)
    {
        var index = (int)(_sequence % _events.Length);
        _events[index] = entry with { Sequence = ++_sequence, Timestamp = Stopwatch.GetTimestamp() };
    }

    public MeshLifecycleSnapshot Snapshot()
    {
        lock (_gate)
            return new(_sequence, Math.Max(0, _sequence - _events.Length), _cancelled, _superseded,
                _buildFailures, _active[(int)MeshLifecycleStage.Queued], _active[(int)MeshLifecycleStage.Snapshotting],
                _active[(int)MeshLifecycleStage.WorkerQueued], _active[(int)MeshLifecycleStage.Building],
                _active[(int)MeshLifecycleStage.AwaitingUpload], _active[(int)MeshLifecycleStage.Uploaded],
                _drawRecorded, _emptyReady);
    }

    public MeshLifecycleEvent[] ReadEvents()
    {
        lock (_gate)
        {
            var count = (int)Math.Min(_sequence, _events.Length);
            var result = new MeshLifecycleEvent[count];
            var start = _sequence - count;
            for (var i = 0; i < count; i++) result[i] = _events[(int)((start + i) % _events.Length)];
            return result;
        }
    }

    public string CreateDump()
    {
        MeshLifecycleSnapshot snapshot;
        MeshLifecycleEvent[] events;
        lock (_gate)
        {
            snapshot = Snapshot();
            events = ReadEvents();
        }
        var text = new StringBuilder();
        text.AppendLine("# Bounded recent history; times are monotonic ms since recorder creation. DrawRecorded is not GPU completion.");
        text.Append("# counters ").AppendLine(snapshot.ToString());
        text.AppendLine("sequence\ttimeMs\tsectionId\trequestId\tx\ty\tz\tepoch\tpriority\tdirtyReasons\tstage\treason");
        foreach (var e in events)
            text.Append(e.Sequence).Append('\t')
                .Append(((e.Timestamp - _origin) * 1000.0 / Stopwatch.Frequency).ToString("F3", CultureInfo.InvariantCulture)).Append('\t')
                .Append(e.SectionId).Append('\t').Append(e.RequestId).Append('\t')
                .Append(e.Position.X).Append('\t').Append(e.Position.Y).Append('\t').Append(e.Position.Z).Append('\t')
                .Append(e.Epoch).Append('\t').Append(e.Priority).Append('\t').Append(e.DirtyReasons).Append('\t')
                .Append(e.Stage).Append('\t').Append(e.Reason).AppendLine();
        return text.ToString();
    }

    public string Describe(MeshLifecycleRequest? request)
    {
        lock (_gate)
        {
            if (request == null) return "0\tNone\t0\t0";
            var now = Stopwatch.GetTimestamp();
            return FormattableString.Invariant($"{request.Id}\t{request.Stage}\t{(now - request.QueuedAt) * 1000.0 / Stopwatch.Frequency:F3}\t{(now - request.StageAt) * 1000.0 / Stopwatch.Frequency:F3}");
        }
    }
}
