using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     One near-terrain quad stream stored inside a region-owned WebGPU arena. LOD and other
///     independent renderers intentionally keep using dedicated <see cref="WgpuMesh" /> buffers.
/// </summary>
internal sealed unsafe class TerrainChunkQuadMesh : IDisposable
{
    private readonly WebGpuDevice _device;
    private TerrainGpuVertexLease? _vertices;

    private TerrainChunkQuadMesh(WebGpuDevice device, TerrainGpuVertexLease vertices, uint vertexCount)
    {
        _device = device;
        _vertices = vertices;
        VertexCount = vertexCount;
    }

    public uint VertexCount { get; }

    public static TerrainChunkQuadMesh Create(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        ReadOnlySpan<ChunkVertex> vertices,
        ReadOnlySpan<ChunkLightVertex> lighting)
    {
        if (vertices.IsEmpty || vertices.Length % 4 != 0)
            throw new ArgumentException("A terrain quad mesh requires a non-empty multiple of four vertices.", nameof(vertices));

        if (lighting.Length != vertices.Length)
            throw new ArgumentException(
                "A terrain quad mesh requires one light value per vertex.", nameof(lighting));

        var allocation = arenas.Upload(regionKey, vertices, lighting);
        try
        {
            device.QuadIndices.EnsureCapacity((uint)vertices.Length / 4);
            return new TerrainChunkQuadMesh(device, allocation, (uint)vertices.Length);
        }
        catch
        {
            allocation.Dispose();
            throw;
        }
    }

    public bool BindChunkQuadStreams(
        RenderPassEncoder* pass,
        ref TerrainStreamBindingState binding)
    {
        var slice = VertexSlice();
        return binding.BindQuads(pass, _device, slice, VertexCount / 4);
    }

    public void DrawBoundQuadRange(
        RenderPassEncoder* pass,
        uint firstQuad,
        uint quadCount,
        uint instanceCount = 1,
        uint firstInstance = 0)
    {
        if (firstQuad + quadCount > VertexCount / 4)
            throw new ArgumentOutOfRangeException(nameof(quadCount), "Quad range exceeds this mesh.");
        _device.QuadIndices.DrawBoundRange(
            pass, firstQuad, quadCount, instanceCount, VertexSlice().FirstVertex, firstInstance);
    }

    public bool DrawQuadWireframe(
        RenderPassEncoder* pass,
        ref TerrainStreamBindingState binding,
        uint firstInstance = 0)
    {
        var slice = VertexSlice();
        var changed = binding.BindWireframe(pass, _device, slice, VertexCount / 4);
        _device.QuadWireframeIndices.DrawBoundRange(
            pass, 0, VertexCount / 4, 1, slice.FirstVertex, firstInstance);
        return changed;
    }

    public void RewriteLighting(ReadOnlySpan<ChunkLightVertex> values) => (_vertices ??
        throw new ObjectDisposedException(nameof(TerrainChunkQuadMesh))).RewriteLighting(values);

    public void Dispose() => Interlocked.Exchange(ref _vertices, null)?.Dispose();

    private TerrainGpuVertexSlice VertexSlice() => (_vertices ??
        throw new ObjectDisposedException(nameof(TerrainChunkQuadMesh))).Slice;
}

/// <summary>Per-pass cache for regional vertex and shared-index bindings.</summary>
internal unsafe struct TerrainStreamBindingState
{
    private nint _geometry;
    private nint _lighting;
    private bool _quadIndicesBound;
    private bool _wireframeIndicesBound;

    public bool BindQuads(
        RenderPassEncoder* pass,
        WebGpuDevice device,
        in TerrainGpuVertexSlice slice,
        uint requiredQuads)
    {
        var changed = BindStreams(pass, device, slice);
        if (!_quadIndicesBound || requiredQuads > device.QuadIndices.QuadCapacity)
        {
            device.QuadIndices.Bind(pass, requiredQuads);
            _quadIndicesBound = true;
        }
        return changed;
    }

    public bool BindWireframe(
        RenderPassEncoder* pass,
        WebGpuDevice device,
        in TerrainGpuVertexSlice slice,
        uint requiredQuads)
    {
        var changed = BindStreams(pass, device, slice);
        if (!_wireframeIndicesBound || requiredQuads > device.QuadWireframeIndices.QuadCapacity)
        {
            device.QuadWireframeIndices.Bind(pass, requiredQuads);
            _wireframeIndicesBound = true;
        }
        return changed;
    }

    private bool BindStreams(
        RenderPassEncoder* pass,
        WebGpuDevice device,
        in TerrainGpuVertexSlice slice)
    {
        var geometry = (nint)slice.GeometryBuffer;
        var lighting = (nint)slice.LightingBuffer;
        if (_geometry == geometry && _lighting == lighting) return false;

        device.Api.RenderPassEncoderSetVertexBuffer(
            pass, 0, slice.GeometryBuffer, 0, WgpuWholeSize.Value);
        device.Api.RenderPassEncoderSetVertexBuffer(
            pass, 1, slice.LightingBuffer, 0, WgpuWholeSize.Value);
        _geometry = geometry;
        _lighting = lighting;
        return true;
    }
}
