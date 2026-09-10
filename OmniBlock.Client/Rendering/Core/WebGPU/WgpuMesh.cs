using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

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
internal static class WgpuWholeSize
{
    public const ulong Value = ulong.MaxValue;
}

/// <summary>
///     A vertex buffer and either owned, shared, or no indices, with the draw call that submits it.
/// </summary>
/// <remarks>
///     Upload once, draw many times — the WebGPU equivalent of a GL display list or VAO. The
///     vertex count and index count are fixed at upload time; the caller supplies the topology
///     and the pipeline per draw.
/// </remarks>
public sealed unsafe class WgpuMesh : IDisposable
{
    /// <summary>The stride of a <see cref="ChunkVertex" />, in bytes.</summary>
    public const uint ChunkVertexStride = 20;

    private readonly WebGpuDevice _device;

    private bool _disposed;
    private bool _usesSharedQuadIndices;

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
        _usesSharedQuadIndices = false;

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
        _usesSharedQuadIndices = false;

        VertexBuffer = CreateBuffer(device, vertexData, BufferUsage.Vertex | BufferUsage.CopyDst);
        IndexBuffer = CreateBuffer(device, indexData, BufferUsage.Index | BufferUsage.CopyDst);
    }

    public WgpuBuffer* VertexBuffer { get; }
    public WgpuBuffer* IndexBuffer { get; }

    /// <summary>How many vertices the mesh stores.</summary>
    public uint VertexCount { get; }

    /// <summary>How many owned indices to draw; shared quad indices are derived from <see cref="VertexCount" />.</summary>
    public uint IndexCount { get; }

    /// <summary>The type of each index in <see cref="IndexBuffer" />.</summary>
    public IndexFormat IndexFormat { get; }

    /// <summary>The primitive topology, so the caller does not have to remember.</summary>
    public PrimitiveTopology Topology { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Chunk meshes can be replaced after the current frame was submitted and while the next
        // encoder is already being assembled. Destroying immediately lets wgpu-native recycle a
        // handle still referenced by recorded draws; if the replacement is smaller, validation
        // then reports that the old vertex count exceeds the newly bound buffer. Retiring through
        // the device keeps both handles alive until a presentation boundary.
        WgpuRelease.DeferredBuffers(_device, (nint)VertexBuffer, (nint)IndexBuffer);
    }

    /// <summary>
    ///     Creates a vertex buffer from a span of <see cref="ChunkVertex" /> structs, using the
    ///     stride the chunk pipeline expects.
    /// </summary>
    public static WgpuMesh FromChunkVertices(WebGpuDevice device, ReadOnlySpan<ChunkVertex> vertices,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList) =>
        new(device, MemoryMarshal.AsBytes(vertices), ChunkVertexStride, topology);

    /// <summary>
    ///     Creates a terrain mesh containing four unique vertices per quad. All such meshes on the
    ///     device share its growable sequential index buffer.
    /// </summary>
    public static WgpuMesh FromChunkQuads(WebGpuDevice device, ReadOnlySpan<ChunkVertex> vertices)
    {
        if (vertices.Length % 4 != 0)
        {
            throw new ArgumentException("A chunk quad mesh requires four vertices per quad.", nameof(vertices));
        }

        var mesh = new WgpuMesh(device, MemoryMarshal.AsBytes(vertices), ChunkVertexStride)
        {
            _usesSharedQuadIndices = true
        };
        device.QuadIndices.EnsureCapacity(mesh.VertexCount / 4);
        return mesh;
    }

    /// <summary>Records the draw command on the current render pass.</summary>
    public void Draw(RenderPassEncoder* pass, uint instanceCount = 1)
    {
        var api = _device.Api;

        api.RenderPassEncoderSetVertexBuffer(pass, 0, VertexBuffer, 0, WgpuWholeSize.Value);

        if (_usesSharedQuadIndices)
        {
            _device.QuadIndices.BindAndDraw(pass, VertexCount / 4, instanceCount);
        }
        else if (IndexBuffer is not null)
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
            Size = (ulong)data.Length
        };

        var buffer = device.Api.DeviceCreateBuffer(device.Device, in descriptor);

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
        _ => 2
    };
}
