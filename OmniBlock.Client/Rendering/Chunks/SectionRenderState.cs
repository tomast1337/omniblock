using OmniBlock.Util;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

[Flags]
internal enum SectionDirtyReason : byte
{
    None = 0,
    InitialTerrain = 1 << 0,
    StreamingBoundary = 1 << 1,
    BlockChange = 1 << 2,
    Lighting = 1 << 3,
    Maintenance = 1 << 4
}

/// <summary>
///     Authoritative main-thread lifecycle state for one render section. Scheduling collections
///     contain only its position; version, request metadata, and the resident mesh live here.
/// </summary>
internal sealed class SectionRenderState(Vector3D<int> position) : IDisposable
{
    public Vector3D<int> Position { get; } = position;
    public ChunkMeshVersion Version { get; } = ChunkMeshVersion.Get();
    public SubChunkRenderer? Renderer { get; private set; }
    public bool IsLit { get; set; }
    public MeshWorkPriority RequestedPriority { get; private set; } = MeshWorkPriority.Background;
    public long RequestedAt { get; private set; } = -1;
    public SectionDirtyReason DirtyReasons { get; private set; }
    public SectionDirtyReason DeferredDirtyReasons { get; private set; }
    public long DeferredAt { get; private set; } = -1;
    public long FirstUploadedAt { get; private set; } = -1;
    public long LastUploadedAt { get; private set; } = -1;

    public void RememberRequest(
        SectionDirtyReason reason,
        MeshWorkPriority priority,
        long requestedAt)
    {
        DirtyReasons |= reason;
        if (RequestedAt < 0) RequestedAt = requestedAt;
        if (priority > RequestedPriority) RequestedPriority = priority;
    }

    public void ClearRequest()
    {
        RequestedPriority = MeshWorkPriority.Background;
        RequestedAt = -1;
        DirtyReasons = SectionDirtyReason.None;
    }

    public void AbandonRequest()
    {
        Version.AbandonPendingMesh();
        ClearRequest();
    }

    /// <summary>
    ///     Records invalidation without creating a mesh epoch or snapshot. Streaming-neighbor
    ///     arrivals use this path so repeated boundary notifications collapse into one eventual
    ///     cleanup build and cannot flood the active worker backlog.
    /// </summary>
    public void DeferRequest(SectionDirtyReason reason, long requestedAt)
    {
        DeferredDirtyReasons |= reason;
        if (DeferredAt < 0) DeferredAt = requestedAt;
    }

    public bool TryConsumeDeferredRequest(out SectionDirtyReason reasons, out long requestedAt)
    {
        reasons = DeferredDirtyReasons;
        requestedAt = DeferredAt;
        if (reasons == SectionDirtyReason.None) return false;

        DeferredDirtyReasons = SectionDirtyReason.None;
        DeferredAt = -1;
        return true;
    }

    public void Install(SubChunkRenderer renderer, bool isLit)
    {
        Renderer = renderer;
        IsLit = isLit;
    }

    public void RecordUploaded(long uploadedAt)
    {
        if (FirstUploadedAt < 0) FirstUploadedAt = uploadedAt;
        LastUploadedAt = uploadedAt;
    }

    public SubChunkRenderer? DetachRenderer()
    {
        var renderer = Renderer;
        Renderer = null;
        return renderer;
    }

    public void Dispose()
    {
        Renderer?.Dispose();
        Renderer = null;
        Version.Release();
    }
}
