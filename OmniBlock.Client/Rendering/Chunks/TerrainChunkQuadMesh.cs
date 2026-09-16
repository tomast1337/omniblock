using System.Runtime.InteropServices;
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
    private TerrainGpuBufferLease? _geometry;

    private TerrainChunkQuadMesh(WebGpuDevice device, TerrainGpuBufferLease geometry, uint vertexCount)
    {
        _device = device;
        _geometry = geometry;
        VertexCount = vertexCount;
    }

    public uint VertexCount { get; }

    public static TerrainChunkQuadMesh Create(
        WebGpuDevice device,
        TerrainGpuArenaSet arenas,
        TerrainRenderRegionKey regionKey,
        ReadOnlySpan<ChunkVertex> vertices)
    {
        if (vertices.IsEmpty || vertices.Length % 4 != 0)
            throw new ArgumentException("A terrain quad mesh requires a non-empty multiple of four vertices.", nameof(vertices));

        var geometry = arenas.Upload(
            regionKey, TerrainGpuStreamKind.Geometry, MemoryMarshal.AsBytes(vertices));
        try
        {
            device.QuadIndices.EnsureCapacity((uint)vertices.Length / 4);
            return new TerrainChunkQuadMesh(device, geometry, (uint)vertices.Length);
        }
        catch
        {
            geometry.Dispose();
            throw;
        }
    }

    public void BindChunkQuadStreams(RenderPassEncoder* pass, TerrainGpuBufferSlice lighting)
    {
        var geometry = GeometrySlice();
        var api = _device.Api;
        api.RenderPassEncoderSetVertexBuffer(
            pass, 0, geometry.Buffer, geometry.OffsetBytes, geometry.LengthBytes);
        if (!lighting.IsEmpty)
            api.RenderPassEncoderSetVertexBuffer(
                pass, 1, lighting.Buffer, lighting.OffsetBytes, lighting.LengthBytes);
        _device.QuadIndices.Bind(pass, VertexCount / 4);
    }

    public void DrawBoundQuadRange(
        RenderPassEncoder* pass,
        uint firstQuad,
        uint quadCount,
        uint instanceCount = 1)
    {
        if (firstQuad + quadCount > VertexCount / 4)
            throw new ArgumentOutOfRangeException(nameof(quadCount), "Quad range exceeds this mesh.");
        _device.QuadIndices.DrawBoundRange(pass, firstQuad, quadCount, instanceCount);
    }

    public void DrawQuadWireframe(RenderPassEncoder* pass, TerrainGpuBufferSlice lighting)
    {
        var geometry = GeometrySlice();
        var api = _device.Api;
        api.RenderPassEncoderSetVertexBuffer(
            pass, 0, geometry.Buffer, geometry.OffsetBytes, geometry.LengthBytes);
        if (!lighting.IsEmpty)
            api.RenderPassEncoderSetVertexBuffer(
                pass, 1, lighting.Buffer, lighting.OffsetBytes, lighting.LengthBytes);
        _device.QuadWireframeIndices.BindAndDraw(pass, VertexCount / 4, 1);
    }

    public void Dispose() => Interlocked.Exchange(ref _geometry, null)?.Dispose();

    private TerrainGpuBufferSlice GeometrySlice() => (_geometry ??
        throw new ObjectDisposedException(nameof(TerrainChunkQuadMesh))).Slice;
}
