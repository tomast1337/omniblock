using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util;
using Silk.NET.WebGPU;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     One immutable, fully prepared presentation of a render section. A candidate owns every GPU
///     resource and every piece of mesh-derived metadata before it becomes visible to the renderer.
/// </summary>
internal sealed class SectionPresentation : IDisposable
{
    private bool _disposed;
    private MeshLifecycleRequest? _firstDrawTrace;
    private SectionLighting? _lighting;

    private SectionPresentation(
        WgpuMesh? solid,
        WgpuMesh? translucent,
        SectionLighting? lighting,
        int solidVertexCount,
        int translucentVertexCount,
        ChunkVisibilityStore visibilityData,
        bool isLit,
        long epoch,
        MeshLifecycleDiagnostics? lifecycle,
        MeshLifecycleRequest? firstDrawTrace)
    {
        Solid = solid;
        Translucent = translucent;
        _lighting = lighting;
        SolidVertexCount = solidVertexCount;
        TranslucentVertexCount = translucentVertexCount;
        VisibilityData = visibilityData;
        IsLit = isLit;
        Epoch = epoch;
        Lifecycle = lifecycle;
        _firstDrawTrace = IsEmpty ? null : firstDrawTrace;
    }

    public WgpuMesh? Solid { get; }
    public WgpuMesh? Translucent { get; }
    public int SolidVertexCount { get; }
    public int TranslucentVertexCount { get; }
    public ChunkVisibilityStore VisibilityData { get; }
    public bool IsLit { get; }
    public long Epoch { get; }
    public bool HasTranslucentMesh => TranslucentVertexCount > 0;
    public bool IsEmpty => SolidVertexCount == 0 && TranslucentVertexCount == 0;
    public int SolidMeshSizeBytes => SolidVertexCount * (int)(WgpuMesh.ChunkVertexStride + WgpuMesh.ChunkLightVertexStride);
    public int TranslucentMeshSizeBytes => TranslucentVertexCount * (int)(WgpuMesh.ChunkVertexStride + WgpuMesh.ChunkLightVertexStride);
    internal MeshLifecycleDiagnostics? Lifecycle { get; }
    internal bool IsDisposed => _disposed;
    public long LightingEpoch => _lighting?.Epoch ?? -1;

    /// <summary>
    ///     Builds every GPU resource before returning the candidate. Failure disposes the partial
    ///     candidate and leaves the currently installed presentation completely untouched.
    /// </summary>
    public static SectionPresentation Create(
        WebGpuDevice device,
        PooledList<ChunkVertex>? solidVertices,
        PooledList<ChunkVertex>? translucentVertices,
        SectionLightModel? solidLighting,
        SectionLightModel? translucentLighting,
        ChunkVisibilityStore visibilityData,
        bool isLit,
        long epoch,
        MeshLifecycleDiagnostics? lifecycle,
        MeshLifecycleRequest? firstDrawTrace)
    {
        WgpuMesh? solid = null;
        WgpuMesh? translucent = null;
        SectionLighting? lighting = null;
        var solidCount = solidVertices?.Count ?? 0;
        var translucentCount = translucentVertices?.Count ?? 0;

        try
        {
            if (solidCount != (solidLighting?.VertexCount ?? 0) ||
                translucentCount != (translucentLighting?.VertexCount ?? 0))
                throw new ArgumentException("Terrain geometry and light models must have matching vertex counts.");

            if (solidCount > 0)
                solid = WgpuMesh.FromChunkQuads(device, solidVertices!.Span);

            if (translucentCount > 0)
                translucent = WgpuMesh.FromChunkQuads(device, translucentVertices!.Span);

            // Lighting is a distinct vertex stream. Its first snapshot is prepared with the rest
            // of the candidate so publication remains atomic; subsequent snapshots replace only
            // these buffers and leave the geometry meshes and their epoch untouched.
            lighting = SectionLighting.CreateInitial(
                device, solidLighting, translucentLighting);

            return new SectionPresentation(
                solid, translucent, lighting,
                solidCount, translucentCount,
                visibilityData, isLit, epoch,
                lifecycle, firstDrawTrace);
        }
        catch
        {
            solid?.Dispose();
            translucent?.Dispose();
            lighting?.Dispose();
            throw;
        }
        finally
        {
            solidVertices?.Dispose();
            translucentVertices?.Dispose();
        }
    }

    internal static SectionPresentation MetadataOnly(
        long epoch = -1,
        ChunkVisibilityStore visibilityData = default,
        bool isLit = false,
        int solidVertexCount = 0,
        int translucentVertexCount = 0) =>
        new(
            null, null, null,
            solidVertexCount, translucentVertexCount,
            visibilityData, isLit, epoch,
            null, null);

    /// <summary>Builds and atomically publishes a replacement light snapshot only.</summary>
    public void RefreshLighting(WebGpuDevice device, ILightProvider provider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _lighting;
        if (current == null) return;
        var replacement = current.Refresh(provider);
        Interlocked.Exchange(ref _lighting, replacement)?.Dispose();
    }

    public unsafe Silk.NET.WebGPU.Buffer* LightBufferFor(int pass)
    {
        var lighting = _lighting;
        return lighting == null ? null : pass == 0 ? lighting.Solid : lighting.Translucent;
    }

    public void RecordFirstDraw()
    {
        var trace = _firstDrawTrace;
        if (trace == null) return;
        _firstDrawTrace = null;
        Lifecycle?.Move(trace, MeshLifecycleStage.DrawRecorded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Solid?.Dispose();
        Translucent?.Dispose();
        Interlocked.Exchange(ref _lighting, null)?.Dispose();
    }
}
