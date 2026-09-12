using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>One bounded RGBA8 readback. Record copy, submit, start mapping, poll without waiting.</summary>
internal sealed unsafe class WgpuAtlasReadback : IDisposable
{
    private readonly WebGpuDevice _device;
    private readonly WgpuBuffer* _buffer;
    private readonly uint _width, _height, _rowBytes;
    private readonly PfnBufferMapCallback _callback;
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _status = -1;
    private bool _mapping, _disposed;
    private GCHandle _keepAlive;
    private static int s_pendingCallbacks;
    internal static int PendingCallbacks => Volatile.Read(ref s_pendingCallbacks);

    public WgpuAtlasReadback(WebGpuDevice device, CommandEncoder* encoder, WgpuTexture texture)
    {
        _device = device; _width = texture.Width; _height = texture.Height;
        // Two-layer sheep atlases store base and fleece separately in one bounded image.
        if ((long)_width * _height * 4 > 8 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(texture));
        _rowBytes = (_width * 4 + 255) & ~255u;
        BufferDescriptor desc = new() { Size = _rowBytes * _height, Usage = BufferUsage.CopyDst | BufferUsage.MapRead };
        _buffer = device.Api.DeviceCreateBuffer(device.Device, in desc);
        _callback = new PfnBufferMapCallback((status, _) =>
        {
            Volatile.Write(ref _status, (int)status);
            if (_keepAlive.IsAllocated) _keepAlive.Free();
            Interlocked.Decrement(ref s_pendingCallbacks);
        });
        ImageCopyTexture source = new() { Texture = texture.Texture, Aspect = TextureAspect.All };
        ImageCopyBuffer destination = new() { Buffer = _buffer, Layout = new TextureDataLayout { BytesPerRow = _rowBytes, RowsPerImage = _height } };
        device.Api.CommandEncoderCopyTextureToBuffer(encoder, in source, in destination, new Extent3D(_width, _height, 1));
    }

    public void AfterSubmit()
    {
        if (_mapping || _disposed) return;
        _mapping = true;
        _keepAlive = GCHandle.Alloc(this); // native callback must survive cancellation and GC
        Interlocked.Increment(ref s_pendingCallbacks);
        _device.Api.BufferMapAsync(_buffer, MapMode.Read, 0, _rowBytes * _height, _callback, null);
    }

    public bool TryComplete(out byte[]? pixels)
    {
        pixels = null;
        if (_disposed) return true;
        _device.PollNonBlocking();
        var status = Volatile.Read(ref _status);
        if (status == -1)
        {
            if (Stopwatch.GetElapsedTime(_started).TotalSeconds <= 10) return false;
            Dispose(); return true; // persistence timeout never makes a valid atlas unavailable
        }
        if (status == (int)BufferMapAsyncStatus.Success)
        {
            var pointer = _device.Api.BufferGetConstMappedRange(_buffer, 0, _rowBytes * _height);
            if (pointer != null)
            {
                pixels = new byte[_width * _height * 4];
                var padded = new ReadOnlySpan<byte>(pointer, checked((int)(_rowBytes * _height)));
                CopyRows(padded, pixels, checked((int)_width), checked((int)_height), checked((int)_rowBytes));
            }
        }
        Dispose(); return true;
    }

    internal static void CopyRows(ReadOnlySpan<byte> padded, Span<byte> pixels, int width, int height, int rowBytes)
    {
        if (width <= 0 || height <= 0 || rowBytes < checked(width * 4) || padded.Length != checked(rowBytes * height) || pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid RGBA8 row layout.");
        for (var y = 0; y < height; y++) padded.Slice(y * rowBytes, width * 4).CopyTo(pixels.Slice(y * width * 4));
        // Texture is explicitly RGBA8, top-to-bottom. No screenshot flip/BGRA conversion here.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var buffer = (nint)_buffer; var api = _device.Api;
        _device.Retire(() =>
        {
            api.BufferUnmap((WgpuBuffer*)buffer);
            api.BufferDestroy((WgpuBuffer*)buffer); api.BufferRelease((WgpuBuffer*)buffer);
        });
    }
}
