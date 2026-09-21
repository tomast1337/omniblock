using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
///     Cooperative cancellation and retained-result ceiling shared by spatial body and seam
///     builders. Each quad reserves its final packed vertex and light bytes before either stream
///     grows, so an unpublishable candidate stops allocating immediately.
/// </summary>
internal sealed class TerrainLodSpatialMeshBuildGuard
{
    private const long BytesPerQuad = 4L *
        (WgpuMesh.ChunkVertexStride + WgpuMesh.ChunkLightVertexStride);
    private readonly CancellationToken _cancellationToken;
    private readonly long _maximumResultBytes;
    private long _reservedResultBytes;

    public TerrainLodSpatialMeshBuildGuard(
        CancellationToken cancellationToken,
        long maximumResultBytes)
    {
        if (maximumResultBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumResultBytes));
        _cancellationToken = cancellationToken;
        _maximumResultBytes = maximumResultBytes;
    }

    public void Checkpoint() => _cancellationToken.ThrowIfCancellationRequested();

    public void ReserveQuad()
    {
        Checkpoint();
        if (_reservedResultBytes > _maximumResultBytes - BytesPerQuad)
            throw new TerrainLodSpatialMeshBudgetExceededException(
                _maximumResultBytes, checked(_reservedResultBytes + BytesPerQuad));
        _reservedResultBytes += BytesPerQuad;
    }
}

internal sealed class TerrainLodSpatialMeshBudgetExceededException(
    long maximumResultBytes,
    long attemptedResultBytes)
    : Exception(
        $"Spatial terrain mesh would retain {attemptedResultBytes:N0} bytes, above its " +
        $"{maximumResultBytes:N0}-byte publication budget.")
{
    public long MaximumResultBytes { get; } = maximumResultBytes;
    public long AttemptedResultBytes { get; } = attemptedResultBytes;
}
