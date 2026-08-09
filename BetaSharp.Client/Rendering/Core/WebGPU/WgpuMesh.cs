using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     Sentinel for "the whole buffer" that wgpu-native accepts without panicking.
/// </summary>
/// <remarks>
///     wgpu-native maps the size parameter to Rust's <c>Option&lt;NonZeroU64&gt;</c>.
///     <c>ulong.MaxValue</c> (<c>WGPU_WHOLE_SIZE</c>) maps to <c>None</c> (use remaining),
///     a valid non-zero value maps to <c>Some(size)</c>, and zero panics the process with
///     <c>invalid size</c> — not a validation error, a hard crash. So every call site that
///     passes a size to <c>SetVertexBuffer</c> or <c>SetIndexBuffer</c> must either know the
///     exact byte count or pass this sentinel.
/// </remarks>
file static class WgpuWholeSize
{
    public const ulong Value = ulong.MaxValue;
}

/// <summary>
///     Vertex and optional index buffers with the draw call that submits them.
/// </summary>
/// <remarks>
///     Upload once, draw many times — the WebGPU equivalent of a GL display list or VAO. The
///     vertex count and index count are fixed at upload time; the caller supplies the topology
///     and the pipeline per draw.
/// </remarks>
public sealed unsafe class WgpuMesh : IDisposable
{
    private readonly WebGpuDevice _device;

    public WgpuBuffer* VertexBuffer { get; }
    public WgpuBuffer* IndexBuffer { get; }

    /// <summary>How many vertices to draw when <see cref="IndexBuffer" /> is null.</summary>
    public uint VertexCount { get; }

    /// <summary>How many indices to draw when <see cref="IndexBuffer" /> is set.</summary>
    public uint IndexCount { get; }

    /// <summary>The type of each index in <see cref="IndexBuffer" />.</summary>
    public IndexFormat IndexFormat { get; }

    /// <summary>The primitive topology, so the caller does not have to remember.</summary>
    public PrimitiveTopology Topology { get; }

    private bool _disposed;

    /// <summary>
    ///     Creates the vertex buffer and uploads its data.
    /// </summary>
    public WgpuMesh(WebGpuDevice device, ReadOnlySpan<byte> vertexData, uint vertexStride, PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        _device = device;
        Topology = topology;
        VertexCount = (uint)(vertexData.Length / vertexStride);
        IndexCount = 0;
        IndexFormat = IndexFormat.Undefined;

        VertexBuffer = CreateBuffer(device, vertexData, BufferUsage.Vertex | BufferUsage.CopyDst);
        IndexBuffer = null;
    }

    /// <summary>
    ///     Creates both vertex and index buffers.
    /// </summary>
    public WgpuMesh(WebGpuDevice device, ReadOnlySpan<byte> vertexData, uint vertexStride, ReadOnlySpan<byte> indexData, IndexFormat indexFormat, PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        _device = device;
        Topology = topology;
        VertexCount = (uint)(vertexData.Length / vertexStride);
        IndexCount = (uint)(indexData.Length / IndexStride(indexFormat));
        IndexFormat = indexFormat;

        VertexBuffer = CreateBuffer(device, vertexData, BufferUsage.Vertex | BufferUsage.CopyDst);
        IndexBuffer = CreateBuffer(device, indexData, BufferUsage.Index | BufferUsage.CopyDst);
    }

    /// <summary>
    ///     Creates a vertex buffer from a span of <see cref="ChunkVertex" /> structs, using the
    ///     stride the chunk pipeline expects.
    /// </summary>
    public static WgpuMesh FromChunkVertices(WebGpuDevice device, ReadOnlySpan<ChunkVertex> vertices,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        return new WgpuMesh(device, MemoryMarshal.AsBytes(vertices), ChunkVertexStride, topology);
    }

    /// <summary>The stride of a <see cref="ChunkVertex" />, in bytes.</summary>
    public const uint ChunkVertexStride = 20;

    /// <summary>Records the draw command on the current render pass.</summary>
    public void Draw(RenderPassEncoder* pass, uint instanceCount = 1)
    {
        Silk.NET.WebGPU.WebGPU api = _device.Api;

        api.RenderPassEncoderSetVertexBuffer(pass, 0, VertexBuffer, 0, WgpuWholeSize.Value);

        if (IndexBuffer is not null)
        {
            api.RenderPassEncoderSetIndexBuffer(pass, IndexBuffer, IndexFormat, 0, WgpuWholeSize.Value);
            api.RenderPassEncoderDrawIndexed(pass, IndexCount, instanceCount, 0, 0, 0);
        }
        else
        {
            api.RenderPassEncoderDraw(pass, VertexCount, instanceCount, 0, 0);
        }
    }

    /// <summary>
    ///     Draws a sub-range of vertices with instancing. Used when multiple buckets share one
    ///     static geometry buffer — each bucket draws its own range with its own instance count.
    /// </summary>
    public void DrawRange(RenderPassEncoder* pass, uint firstVertex, uint vertexCount, uint instanceCount, uint firstInstance = 0)
    {
        _device.Api.RenderPassEncoderSetVertexBuffer(pass, 0, VertexBuffer, 0, WgpuWholeSize.Value);
        _device.Api.RenderPassEncoderDraw(pass, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    private static WgpuBuffer* CreateBuffer(WebGpuDevice device, ReadOnlySpan<byte> data, BufferUsage usage)
    {
        BufferDescriptor descriptor = new()
        {
            Usage = usage,
            Size = (ulong)data.Length,
        };

        WgpuBuffer* buffer = device.Api.DeviceCreateBuffer(device.Device, in descriptor);

        fixed (byte* p = data)
        {
            device.Api.QueueWriteBuffer(device.Queue, buffer, 0, p, (nuint)data.Length);
        }

        return buffer;
    }

    private static uint IndexStride(IndexFormat format) => format switch
    {
        IndexFormat.Uint16 => 2,
        IndexFormat.Uint32 => 4,
        _ => 2,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;

        if (VertexBuffer is not null)
        {
            api.BufferDestroy(VertexBuffer);
            api.BufferRelease(VertexBuffer);
        }

        if (IndexBuffer is not null)
        {
            api.BufferDestroy(IndexBuffer);
            api.BufferRelease(IndexBuffer);
        }
    }
}
