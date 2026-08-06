using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

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

    /// <summary>Records the draw command on the current render pass.</summary>
    public void Draw(RenderPassEncoder* pass, uint instanceCount = 1)
    {
        Silk.NET.WebGPU.WebGPU api = _device.Api;

        api.RenderPassEncoderSetVertexBuffer(pass, 0, VertexBuffer, 0, 0);

        if (IndexBuffer is not null)
        {
            api.RenderPassEncoderSetIndexBuffer(pass, IndexBuffer, IndexFormat, 0, 0);
            api.RenderPassEncoderDrawIndexed(pass, IndexCount, instanceCount, 0, 0, 0);
        }
        else
        {
            api.RenderPassEncoderDraw(pass, VertexCount, instanceCount, 0, 0);
        }
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
