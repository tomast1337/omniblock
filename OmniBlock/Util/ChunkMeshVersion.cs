namespace OmniBlock.Util;

public class ChunkMeshVersion
{
    private static readonly Stack<ChunkMeshVersion> s_pool = new();

    private long _epoch;
    private long _lastMeshed;
    private long _pendingMesh = -1;

    private ChunkMeshVersion() => TotalAllocated++;

    public static int TotalAllocated { get; private set; }
    public static int TotalReleased { get; private set; }

    /// <summary>
    ///     How many times this chunk has been marked dirty, how far the last finished mesh got, and
    ///     which epoch a mesh is being built for, or -1 for none.
    /// </summary>
    /// <remarks>
    ///     For the debug view. A chunk whose epoch has outrun its last mesh with nothing pending is
    ///     one the world has changed and the screen has not caught up with.
    /// </remarks>
    public (long Epoch, long LastMeshed, long Pending) State => (_epoch, _lastMeshed, _pendingMesh);

    public static void ClearPool()
    {
        s_pool.Clear();
        TotalAllocated = 0;
        TotalReleased = 0;
    }

    public static ChunkMeshVersion Get()
    {
        if (s_pool.TryPop(out var version))
        {
            version._epoch = 0;
            version._lastMeshed = 0;
            version._pendingMesh = -1;
            return version;
        }

        return new ChunkMeshVersion();
    }

    public void Release()
    {
        TotalReleased++;
        s_pool.Push(this);
    }

    public void MarkDirty() => _epoch++;

    public long? SnapshotIfNeeded()
    {
        if (_epoch != _lastMeshed && _pendingMesh == -1)
        {
            _pendingMesh = _epoch;
            return _epoch;
        }

        return null;
    }

    /// <summary>
    ///     Advances a revision which is still in the render-thread queue to the latest epoch.
    ///     No world snapshot exists yet, so replacing it loses no work and avoids deliberately
    ///     building a result already known to be stale.
    /// </summary>
    public long ReplaceQueuedSnapshotWithLatest()
    {
        if (_pendingMesh == -1)
            throw new InvalidOperationException("Cannot replace a mesh revision when none is pending.");
        _pendingMesh = _epoch;
        return _pendingMesh;
    }

    public void CompleteMesh(long snapshotEpoch)
    {
        if (_pendingMesh == snapshotEpoch)
        {
            _pendingMesh = -1;
            _lastMeshed = Math.Max(_lastMeshed, snapshotEpoch);
        }
    }

    /// <summary>Releases a cancelled revision without claiming that it produced a mesh.</summary>
    public void CancelMesh(long snapshotEpoch)
    {
        if (_pendingMesh == snapshotEpoch) _pendingMesh = -1;
    }

    /// <summary>
    ///     Makes a lost request eligible for a new snapshot without pretending it completed.
    /// </summary>
    public void AbandonPendingMesh() => _pendingMesh = -1;

    public bool IsStale(long snapshotEpoch) => _epoch > snapshotEpoch;

    public bool IsModified() => _epoch != _lastMeshed;
}
