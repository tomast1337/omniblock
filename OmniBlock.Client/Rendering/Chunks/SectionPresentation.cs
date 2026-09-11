using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     One immutable, fully prepared presentation of a render section. A candidate owns every GPU
///     resource and every piece of mesh-derived metadata before it becomes visible to the renderer.
/// </summary>
internal sealed class SectionPresentation : IDisposable
{
    private bool _disposed;
    private MeshLifecycleRequest? _firstDrawTrace;

    private SectionPresentation(
        WgpuMesh? solid,
        WgpuMesh? translucent,
        WgpuMesh? wireframe,
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
        Wireframe = wireframe;
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
    public WgpuMesh? Wireframe { get; }
    public int SolidVertexCount { get; }
    public int TranslucentVertexCount { get; }
    public ChunkVisibilityStore VisibilityData { get; }
    public bool IsLit { get; }
    public long Epoch { get; }
    public bool HasTranslucentMesh => TranslucentVertexCount > 0;
    public bool IsEmpty => SolidVertexCount == 0 && TranslucentVertexCount == 0;
    public int SolidMeshSizeBytes => SolidVertexCount * (int)WgpuMesh.ChunkVertexStride;
    public int TranslucentMeshSizeBytes => TranslucentVertexCount * (int)WgpuMesh.ChunkVertexStride;
    internal MeshLifecycleDiagnostics? Lifecycle { get; }
    internal bool IsDisposed => _disposed;

    /// <summary>
    ///     Builds every GPU resource before returning the candidate. Failure disposes the partial
    ///     candidate and leaves the currently installed presentation completely untouched.
    /// </summary>
    public static SectionPresentation Create(
        WebGpuDevice device,
        PooledList<ChunkVertex>? solidVertices,
        PooledList<ChunkVertex>? translucentVertices,
        ChunkVisibilityStore visibilityData,
        bool isLit,
        long epoch,
        MeshLifecycleDiagnostics? lifecycle,
        MeshLifecycleRequest? firstDrawTrace)
    {
        WgpuMesh? solid = null;
        WgpuMesh? translucent = null;
        WgpuMesh? wireframe = null;
        var solidCount = solidVertices?.Count ?? 0;
        var translucentCount = translucentVertices?.Count ?? 0;

        try
        {
            if (solidCount > 0)
            {
                var data = solidVertices!.Span;
                solid = WgpuMesh.FromChunkQuads(device, data);
                wireframe = BuildWireframeMesh(device, data);
            }

            if (translucentCount > 0)
                translucent = WgpuMesh.FromChunkQuads(device, translucentVertices!.Span);

            return new SectionPresentation(
                solid, translucent, wireframe,
                solidCount, translucentCount,
                visibilityData, isLit, epoch,
                lifecycle, firstDrawTrace);
        }
        catch
        {
            solid?.Dispose();
            translucent?.Dispose();
            wireframe?.Dispose();
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

    /// <summary>
    ///     Expands four unique vertices per quad into the edges of its two indexed triangles.
    ///     Includes the diagonal, matching the terrain debug wireframe.
    /// </summary>
    private static WgpuMesh? BuildWireframeMesh(WebGpuDevice device, ReadOnlySpan<ChunkVertex> quads)
    {
        if (quads.Length == 0) return null;
        if (quads.Length % 4 != 0)
            throw new ArgumentException("Wireframe terrain input requires four vertices per quad.", nameof(quads));

        var lines = new ChunkVertex[quads.Length * 3];
        var outIdx = 0;
        for (var i = 0; i < quads.Length; i += 4)
        {
            ChunkVertex a = quads[i], b = quads[i + 1], c = quads[i + 2], d = quads[i + 3];
            lines[outIdx++] = a;
            lines[outIdx++] = b;
            lines[outIdx++] = b;
            lines[outIdx++] = c;
            lines[outIdx++] = c;
            lines[outIdx++] = a;
            lines[outIdx++] = c;
            lines[outIdx++] = d;
            lines[outIdx++] = d;
            lines[outIdx++] = a;
            lines[outIdx++] = a;
            lines[outIdx++] = c;
        }

        return WgpuMesh.FromChunkVertices(device, lines, PrimitiveTopology.LineList);
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
        Wireframe?.Dispose();
    }
}
