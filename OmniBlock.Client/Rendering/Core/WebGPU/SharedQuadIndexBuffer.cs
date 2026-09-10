using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     One growable sequential quad index buffer shared by every terrain mesh on a WebGPU device.
/// </summary>
/// <remarks>
///     Chunk meshes store four unique corners per quad. The shared indices expand each group to
///     <c>0,1,2, 2,3,0</c> at draw time without storing duplicate vertices or a per-mesh index buffer.
/// </remarks>
internal sealed unsafe class SharedQuadIndexBuffer : IDisposable
{
    private readonly WebGpuDevice _device;
    private WgpuBuffer* _buffer;
    private uint _quadCapacity;

    public SharedQuadIndexBuffer(WebGpuDevice device) => _device = device;

    public uint QuadCapacity => _quadCapacity;

    public void EnsureCapacity(uint requiredQuads)
    {
        if (requiredQuads <= _quadCapacity) return;

        var capacity = Math.Max(256u, _quadCapacity);
        while (capacity < requiredQuads)
        {
            capacity = checked(capacity * 2);
        }

        var indices = new uint[checked(capacity * 6)];
        FillIndices(indices);

        BufferDescriptor descriptor = new()
        {
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
            Size = checked((ulong)indices.Length * sizeof(uint))
        };

        var replacement = _device.Api.DeviceCreateBuffer(_device.Device, in descriptor);
        fixed (uint* data = indices)
        {
            _device.Api.QueueWriteBuffer(
                _device.Queue, replacement, 0, data, (nuint)(indices.Length * sizeof(uint)));
        }

        var previous = _buffer;
        _buffer = replacement;
        _quadCapacity = capacity;

        if (previous is not null)
        {
            var retired = (nint)previous;
            _device.Retire(() => _device.Api.BufferRelease((WgpuBuffer*)retired));
        }
    }

    public void BindAndDraw(RenderPassEncoder* pass, uint quadCount, uint instanceCount)
    {
        EnsureCapacity(quadCount);
        _device.Api.RenderPassEncoderSetIndexBuffer(
            pass, _buffer, IndexFormat.Uint32, 0, WgpuWholeSize.Value);
        _device.Api.RenderPassEncoderDrawIndexed(pass, checked(quadCount * 6), instanceCount, 0, 0, 0);
    }

    public void Dispose()
    {
        if (_buffer is null) return;
        _device.Api.BufferRelease(_buffer);
        _buffer = null;
        _quadCapacity = 0;
    }

    internal static void FillIndices(Span<uint> destination)
    {
        if (destination.Length % 6 != 0)
        {
            throw new ArgumentException("Quad index storage must contain six indices per quad.", nameof(destination));
        }

        for (var index = 0; index < destination.Length; index += 6)
        {
            var vertex = checked((uint)(index / 6) * 4);
            destination[index] = vertex;
            destination[index + 1] = vertex + 1;
            destination[index + 2] = vertex + 2;
            destination[index + 3] = vertex + 2;
            destination[index + 4] = vertex + 3;
            destination[index + 5] = vertex;
        }
    }
}
