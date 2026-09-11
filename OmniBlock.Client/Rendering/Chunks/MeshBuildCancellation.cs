namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Per-build cooperative cancellation. This is deliberately separate from renderer shutdown:
///     superseding one section must not disturb unrelated workers. Priority is raised atomically
///     with cancellation so the completion marker returns through the lane needed by its replacement.
/// </summary>
internal sealed class MeshBuildCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly object _gate = new();
    private readonly CancellationToken _token;
    private bool _cancelled;
    private bool _disposed;
    private MeshCancellationReason _reason;
    private MeshWorkPriority _priority;

    public MeshBuildCancellation(MeshWorkPriority priority)
    {
        _priority = priority;
        _token = _source.Token;
    }

    public CancellationToken Token => _token;
    public bool IsCancellationRequested
    {
        get { lock (_gate) return _cancelled; }
    }
    public MeshCancellationReason Reason
    {
        get { lock (_gate) return _reason; }
    }
    public MeshWorkPriority Priority
    {
        get { lock (_gate) return _priority; }
    }

    public bool Cancel(MeshCancellationReason reason, MeshWorkPriority replacementPriority)
    {
        if (reason == MeshCancellationReason.None)
            throw new ArgumentException("Cancellation requires a reason.", nameof(reason));
        bool first;
        lock (_gate)
        {
            if (_disposed) return false;
            if (replacementPriority > _priority) _priority = replacementPriority;
            first = _reason == MeshCancellationReason.None;
            if (first)
            {
                _reason = reason;
                _cancelled = true;
                _source.Cancel();
            }
        }
        return first;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _source.Dispose();
        }
    }
}
