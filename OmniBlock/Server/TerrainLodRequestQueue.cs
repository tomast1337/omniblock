using OmniBlock.Worlds.Lod;
using OmniBlock.Network.Messages;

namespace OmniBlock.Server;

internal enum TerrainLodRequestEnqueueResult
{
    Added,
    Duplicate,
    Full
}

internal readonly record struct QueuedTerrainLodRequest(
    int Dimension,
    string CacheIdentity,
    int MaximumSpatialLevel,
    int QualityPolicyVersion,
    TerrainLodTileKey Tile,
    TerrainLodTileStatus? ImmediateStatus = null,
    string Diagnostic = "");

/// <summary>
///     Bounded per-client FIFO for remote LOD disclosure. Requests are deduplicated while queued,
///     so retry traffic cannot displace useful work or grow server memory.
/// </summary>
internal sealed class TerrainLodRequestQueue
{
    public const int Capacity = TerrainLodScaleBudget.MaximumQueuedRequestsPerClient;

    private readonly Queue<QueuedTerrainLodRequest> _requests = new(Capacity);
    private readonly HashSet<QueuedTerrainLodRequest> _queued = new();

    public int Count => _requests.Count;

    public TerrainLodRequestEnqueueResult Enqueue(QueuedTerrainLodRequest request)
    {
        if (_queued.Contains(request)) return TerrainLodRequestEnqueueResult.Duplicate;
        if (_requests.Count >= Capacity) return TerrainLodRequestEnqueueResult.Full;
        _requests.Enqueue(request);
        _queued.Add(request);
        return TerrainLodRequestEnqueueResult.Added;
    }

    public bool TryPeek(out QueuedTerrainLodRequest request) => _requests.TryPeek(out request);

    public bool TryDequeue(out QueuedTerrainLodRequest request)
    {
        if (!_requests.TryDequeue(out request)) return false;
        _queued.Remove(request);
        return true;
    }

    public void Clear()
    {
        _requests.Clear();
        _queued.Clear();
    }
}
