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
    Maintenance = 1 << 4,
    LeadingEdge = 1 << 5
}

[Flags]
internal enum NearFieldRescueReason : byte
{
    None = 0,
    IncompleteAdjacency = 1 << 0,
    NewPresentation = 1 << 1,
    PresentationRegression = 1 << 2
}

/// <summary>
///     Authoritative main-thread lifecycle state for one render section. Scheduling collections
///     contain only its position; version, request metadata, and the resident mesh live here.
/// </summary>
internal sealed class SectionRenderState(Vector3D<int> position, MeshLifecycleDiagnostics? diagnostics = null) : IDisposable
{
    private static long s_nextLifetimeId;
    public long LifetimeId { get; } = Interlocked.Increment(ref s_nextLifetimeId);
    public MeshLifecycleRequest? PendingTrace { get; private set; }
    public MeshLifecycleRequest? ResidentTrace { get; private set; }
    public bool IsDisposed { get; private set; }
    public int OutsideRetentionSinceFrame { get; private set; } = -1;
    public bool OwnsResult(long sectionId) => !IsDisposed && LifetimeId == sectionId;

    public bool ShouldEvict(bool insideRetention, int frame, int graceFrames)
    {
        if (insideRetention)
        {
            OutsideRetentionSinceFrame = -1;
            return false;
        }

        if (OutsideRetentionSinceFrame < 0) OutsideRetentionSinceFrame = frame;
        return frame - OutsideRetentionSinceFrame >= graceFrames;
    }
    public Vector3D<int> Position { get; } = position;
    public ChunkMeshVersion Version { get; } = ChunkMeshVersion.Get();
    public SubChunkRenderer? Renderer { get; private set; }
    public bool IsLit => Renderer?.IsLit == true;
    public MeshWorkPriority RequestedPriority { get; private set; } = MeshWorkPriority.Background;
    public long RequestedAt { get; private set; } = -1;
    public int RequestedDeadlineFrame { get; private set; } = -1;
    public SectionDirtyReason DirtyReasons { get; private set; }
    public SectionDirtyReason DeferredDirtyReasons { get; private set; }
    public long DeferredAt { get; private set; } = -1;
    public long FirstUploadedAt { get; private set; } = -1;
    public long LastUploadedAt { get; private set; } = -1;
    public int PresentationInstalledFrame { get; private set; } = -1;
    public int AdjacencyChangedFrame { get; private set; } = -1;
    public NearFieldRescueReason ActiveRescueReasons { get; private set; }
    public int RescueStartedFrame { get; private set; } = -1;
    public int RescueDurationFrames { get; private set; }

    public void NotePresentationInstalled(int frame) => PresentationInstalledFrame = frame;

    public void NoteAdjacencyChanged(int frame) => AdjacencyChangedFrame = frame;

    public void RecordNearFieldRescue(NearFieldRescueReason reasons, int frame)
    {
        if (reasons == NearFieldRescueReason.None)
        {
            ClearNearFieldRescue();
            return;
        }

        if (ActiveRescueReasons == NearFieldRescueReason.None || frame > RescueStartedFrame + RescueDurationFrames)
            RescueStartedFrame = frame;
        ActiveRescueReasons = reasons;
        RescueDurationFrames = frame - RescueStartedFrame + 1;
    }

    public void ClearNearFieldRescue()
    {
        ActiveRescueReasons = NearFieldRescueReason.None;
        RescueStartedFrame = -1;
        RescueDurationFrames = 0;
    }

    public void RememberRequest(
        SectionDirtyReason reason,
        MeshWorkPriority priority,
        long requestedAt,
        int deadlineFrame = -1)
    {
        DirtyReasons |= reason;
        if (RequestedAt < 0) RequestedAt = requestedAt;
        if (priority > RequestedPriority) RequestedPriority = priority;
        if (deadlineFrame >= 0 &&
            (RequestedDeadlineFrame < 0 || deadlineFrame < RequestedDeadlineFrame))
            RequestedDeadlineFrame = deadlineFrame;
    }

    public void RecordInvalidation(SectionDirtyReason reason) =>
        diagnostics?.Note(LifetimeId, Position, Version.State.Epoch, RequestedPriority, reason, MeshLifecycleStage.Invalidated);

    public MeshLifecycleRequest? BeginTrace(long epoch, int queuedFrame = 0)
    {
        diagnostics?.Cancel(PendingTrace, MeshCancellationReason.Superseded);
        return PendingTrace = diagnostics?.Queue(
            LifetimeId, Position, epoch, RequestedPriority, DirtyReasons,
            queuedFrame, RequestedDeadlineFrame);
    }

    public void PromoteTrace(MeshWorkPriority priority, int deadlineFrame) =>
        diagnostics?.Promote(PendingTrace, priority, deadlineFrame);

    public void ClearRequest()
    {
        RequestedPriority = MeshWorkPriority.Background;
        RequestedAt = -1;
        RequestedDeadlineFrame = -1;
        DirtyReasons = SectionDirtyReason.None;
    }

    public void AbandonRequest(MeshCancellationReason reason = MeshCancellationReason.Orphaned)
    {
        // A queued state may outlive eviction; never touch its returned-to-pool version.
        if (IsDisposed) return;
        diagnostics?.Cancel(PendingTrace, reason);
        PendingTrace = null;
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
        if (DeferredAt < 0)
            diagnostics?.Note(LifetimeId, Position, Version.State.Epoch, RequestedPriority, reason, MeshLifecycleStage.Deferred);
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

    /// <summary>
    ///     Commits the mesh-derived state and its epoch as one render-thread transaction. The
    ///     presentation has already allocated every GPU resource, so nothing in this method is
    ///     expected to fail after the reference exchange.
    /// </summary>
    public void CommitPresentation(SubChunkRenderer renderer, SectionPresentation presentation)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (renderer.Position != Position)
            throw new InvalidOperationException(
                $"Renderer at {renderer.Position} cannot be installed into section {Position}.");
        if (presentation.Epoch != Version.State.Pending)
            throw new InvalidOperationException(
                $"Section {Position} expects pending epoch {Version.State.Pending}, got {presentation.Epoch}.");

        renderer.InstallPresentation(presentation);
        Version.CompleteMesh(presentation.Epoch);
        Renderer = renderer;
    }

    public void RecordUploaded(long uploadedAt, MeshLifecycleRequest? trace = null, bool empty = false)
    {
        diagnostics?.Cancel(ResidentTrace, MeshCancellationReason.Superseded);
        ResidentTrace = trace;
        if (ReferenceEquals(PendingTrace, trace)) PendingTrace = null;
        diagnostics?.Move(trace, MeshLifecycleStage.Uploaded);
        if (empty) diagnostics?.Move(trace, MeshLifecycleStage.EmptyReady);
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
        => Dispose(MeshCancellationReason.OutsideRetention);

    public void Dispose(MeshCancellationReason reason)
    {
        if (IsDisposed) return;
        IsDisposed = true;
        diagnostics?.Cancel(PendingTrace, reason);
        diagnostics?.Cancel(ResidentTrace, reason);
        diagnostics?.Note(LifetimeId, Position, Version.State.Epoch, RequestedPriority,
            DirtyReasons | DeferredDirtyReasons, MeshLifecycleStage.Evicted, reason);
        Renderer?.Dispose();
        Renderer = null;
        Version.Release();
    }
}
