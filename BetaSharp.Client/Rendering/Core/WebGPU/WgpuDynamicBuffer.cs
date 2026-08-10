using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     A GPU buffer whose contents are replaced every frame — the WebGPU equivalent of a GL
///     streaming VBO with <c>GLEnum.StreamDraw</c>.
/// </summary>
/// <remarks>
///     Sized once at construction and reused. <see cref="Write" /> uploads new data through the
///     device queue. The caller sets the buffer on the render pass and records the draw; this
///     type owns only the buffer resource.
/// </remarks>
public sealed unsafe class WgpuDynamicBuffer : IDisposable
{
    public WgpuBuffer* Buffer { get; }
    public ulong Capacity { get; }

    private readonly WebGpuDevice _device;
    private bool _disposed;

    /// <summary>
    ///     Allocates a GPU buffer of <paramref name="capacity"/> bytes with
    ///     <see cref="BufferUsage.Vertex"/> and <see cref="BufferUsage.CopyDst"/>.
    /// </summary>
    public WgpuDynamicBuffer(WebGpuDevice device, ulong capacity)
    {
        _device = device;
        Capacity = capacity;

        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Size = capacity,
        };

        Buffer = device.Api.DeviceCreateBuffer(device.Device, in descriptor);
    }

    /// <summary>Uploads <paramref name="data"/> to the GPU buffer through the queue.</summary>
    public void Write<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ulong byteCount = (ulong)(data.Length * sizeof(T));
        if (byteCount > Capacity)
        {
            throw new ArgumentException(
                $"Data size {byteCount} exceeds capacity {Capacity}.");
        }

        fixed (T* p = data)
        {
            _device.Api.QueueWriteBuffer(_device.Queue, Buffer, 0, p, (nuint)byteCount);
        }
    }

    /// <summary>Binds the buffer at vertex slot 0 on the render pass.</summary>
    public void Bind(RenderPassEncoder* pass)
    {
        _device.Api.RenderPassEncoderSetVertexBuffer(pass, 0, Buffer, 0, WgpuWholeSizeValue);
    }

    private const ulong WgpuWholeSizeValue = ulong.MaxValue;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        if (Buffer is not null)
        {
            api.BufferDestroy(Buffer);
            api.BufferRelease(Buffer);
        }
    }
}
