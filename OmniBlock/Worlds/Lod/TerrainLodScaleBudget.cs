namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Hard resource limits for the distant-terrain scale path. These are correctness limits, not
///     quality targets: callers must coarsen, defer, retain an older presentation, or reject work
///     instead of growing an unbounded collection when a limit is reached.
/// </summary>
public static class TerrainLodScaleBudget
{
    public const int MaximumCoverageTiles = 512;
    public const int MaximumSelectedNodes = 1024;
    public const int MaximumDrawPagesPerLayer = 2048;
    public const int TargetSpatialPresentations = MaximumSelectedNodes + 128;
    public const int MaximumSpatialPresentations = MaximumSelectedNodes * 2 + 256;
    public const int MaximumLeadingEdgePresentations = 128;

    public const int ClientHierarchyTiles = 4096;
    public const int ServerHierarchyTiles = 8192;
    public const int SpatialMeshCompilationItems = 32;
    public const int SpatialMeshCompletedItems = 8;
    public const int SpatialSeamCompilationItems = 64;
    public const int SpatialSeamCompletedItems = 16;
    public const int SpatialUploadsPerFrame = 2;
    public const int SpatialSeamUploadsPerFrame = 2;
    public const long MaximumUploadBytesPerFrame = 8L * 1024 * 1024;
    public const long TargetSpatialGpuBytes = 128L * 1024 * 1024;
    public const long MaximumSpatialGpuBytes = 256L * 1024 * 1024;
    public const int ClientHierarchyTileReserve = 256;

    public const int MaximumRemoteOutstandingRequests = 16;
    public const int MaximumRequestKeys = 8;
    // Apply one complete request batch per render tick. Source publication only updates the
    // bounded CPU hierarchy and mesh-pending set; compilation and GPU upload retain their own
    // stricter budgets below. Keeping this at two made an L8 partition require about 147 costly
    // render frames before the client could even know its complete source set.
    public const int MaximumSourceTilesAdmittedPerFrame = MaximumRequestKeys;
    public const int MaximumSourceStatusesObservedPerFrame = MaximumRequestKeys * 4;
    public const int MaximumCompressedTileBytes = 2 * 1024 * 1024 - 128;
    public const int MaximumDecodedTileBytes = 64 * 1024 * 1024;
    public const int TransportBytesPerSecond = 256 * 1024;
    public const int TransportBurstBytes = 2 * 1024 * 1024;
    public const int MaximumTransportBacklog = 8;
    public const int MaximumQueuedRequestsPerClient = 32;
    public const int GlobalTransportBytesPerSecond = 2 * 1024 * 1024;
    public const int GlobalTransportBurstBytes = 4 * 1024 * 1024;
    public const int MaximumTransportResponsesPerTick = 8;
    // An integrated server ticks independently from the render thread. Expensive cold LOD frames
    // can therefore accumulate several server ticks of already-bounded replies before the client
    // gets another chance to apply them. Loopback application only transfers message ownership to
    // the client's bounded queues, so give it a larger, still finite catch-up ceiling.
    public const int MaximumLoopbackBulkPacketsPerTick = 64;
}
