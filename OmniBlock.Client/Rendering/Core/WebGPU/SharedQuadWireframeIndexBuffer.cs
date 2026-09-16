using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     One lazily grown line-index buffer shared by every quad terrain mesh on a WebGPU device.
/// </summary>
/// <remarks>
///     The wireframe view reuses the ordinary four-corner geometry and light streams. Only this
///     small index buffer is allocated when wireframe is actually drawn, avoiding a three-times
///     expanded vertex and light copy for every resident section while diagnostics are disabled.
/// </remarks>
internal sealed unsafe class SharedQuadWireframeIndexBuffer : IDisposable
{
    private readonly WebGpuDevice _device;
    private WgpuBuffer* _buffer;

    public SharedQuadWireframeIndexBuffer(WebGpuDevice device) => _device = device;

    public uint QuadCapacity { get; private set; }

    public void Dispose()
    {
        if (_buffer is null) return;
        _device.Api.BufferRelease(_buffer);
        _buffer = null;
        QuadCapacity = 0;
    }

    public void EnsureCapacity(uint requiredQuads)
    {
        if (requiredQuads <= QuadCapacity) return;

        var capacity = Math.Max(256u, QuadCapacity);
        while (capacity < requiredQuads) capacity = checked(capacity * 2);

        var indices = new uint[checked(capacity * 12)];
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
        QuadCapacity = capacity;
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
        _device.Api.RenderPassEncoderDrawIndexed(pass, checked(quadCount * 12), instanceCount, 0, 0, 0);
    }

    public void Bind(RenderPassEncoder* pass, uint requiredQuads)
    {
        EnsureCapacity(requiredQuads);
        _device.Api.RenderPassEncoderSetIndexBuffer(
            pass, _buffer, IndexFormat.Uint32, 0, WgpuWholeSize.Value);
    }

    public void DrawBoundRange(
        RenderPassEncoder* pass,
        uint firstQuad,
        uint quadCount,
        uint instanceCount,
        int baseVertex = 0,
        uint firstInstance = 0)
    {
        if (quadCount == 0) return;
        if (firstQuad + quadCount > QuadCapacity)
            throw new ArgumentOutOfRangeException(nameof(quadCount),
                "Quad range exceeds the bound wireframe index buffer.");
        _device.Api.RenderPassEncoderDrawIndexed(
            pass, checked(quadCount * 12), instanceCount,
            checked(firstQuad * 12), baseVertex, firstInstance);
    }

    internal static void FillIndices(Span<uint> destination)
    {
        if (destination.Length % 12 != 0)
            throw new ArgumentException("Quad wireframe index storage must contain twelve indices per quad.", nameof(destination));

        for (var index = 0; index < destination.Length; index += 12)
        {
            var vertex = checked((uint)(index / 12) * 4);
            destination[index] = vertex;
            destination[index + 1] = vertex + 1;
            destination[index + 2] = vertex + 1;
            destination[index + 3] = vertex + 2;
            destination[index + 4] = vertex + 2;
            destination[index + 5] = vertex;
            destination[index + 6] = vertex + 2;
            destination[index + 7] = vertex + 3;
            destination[index + 8] = vertex + 3;
            destination[index + 9] = vertex;
            destination[index + 10] = vertex;
            destination[index + 11] = vertex + 2;
        }
    }
}
