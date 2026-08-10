using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     A shader storage buffer — the WebGPU equivalent of a GL Shader Storage Buffer Object.
///     Holds per-instance data that a vertex shader reads via <c>gl_InstanceID</c> in GLSL or
///     <c>array&lt;T&gt;</c> in WGSL.
/// </summary>
/// <remarks>
///     Created once at the size of the largest possible instance batch. Rewritten each frame via
///     <see cref="Write"/> when the instance count changes.
/// </remarks>
public sealed unsafe class WgpuStorageBuffer : IDisposable
{
    public WgpuBuffer* Buffer { get; }
    public BindGroup* BindGroup { get; }
    public ulong Capacity { get; }

    private readonly WebGpuDevice _device;
    private bool _disposed;

    /// <summary>
    ///     Allocates a storage buffer of <paramref name="capacity"/> bytes and creates a bind group
    ///     at <paramref name="binding"/> within <paramref name="layout"/>.
    /// </summary>
    public WgpuStorageBuffer(WebGpuDevice device, ulong capacity, BindGroupLayout* layout, uint binding = 0)
    {
        _device = device;
        Capacity = capacity;

        BufferDescriptor bufferDesc = new()
        {
            Usage = BufferUsage.Storage | BufferUsage.CopyDst,
            Size = capacity,
        };

        Buffer = device.Api.DeviceCreateBuffer(device.Device, in bufferDesc);

        BindGroupEntry entry = new()
        {
            Binding = binding,
            Buffer = Buffer,
            Offset = 0,
            Size = capacity,
        };

        BindGroupDescriptor groupDesc = new()
        {
            Layout = layout,
            EntryCount = 1,
            Entries = &entry,
        };

        BindGroup = device.Api.DeviceCreateBindGroup(device.Device, in groupDesc);
    }

    /// <summary>Uploads <paramref name="data"/> to the storage buffer through the queue.</summary>
    public void Write<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ulong byteCount = (ulong)(data.Length * sizeof(T));
        if (byteCount > Capacity)
        {
            throw new ArgumentException(
                $"Data size {byteCount} exceeds storage buffer capacity {Capacity}.");
        }

        fixed (T* p = data)
        {
            _device.Api.QueueWriteBuffer(_device.Queue, Buffer, 0, p, (nuint)byteCount);
        }
    }

    /// <summary>Binds the storage buffer's bind group at <paramref name="groupIndex"/> on the pass.</summary>
    public void Bind(RenderPassEncoder* pass, uint groupIndex = 1)
    {
        _device.Api.RenderPassEncoderSetBindGroup(pass, groupIndex, BindGroup, 0, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        if (BindGroup is not null) api.BindGroupRelease(BindGroup);
        if (Buffer is not null)
        {
            api.BufferDestroy(Buffer);
            api.BufferRelease(Buffer);
        }
    }
}
