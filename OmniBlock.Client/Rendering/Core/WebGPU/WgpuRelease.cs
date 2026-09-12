using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     Hands a texture's objects back to wgpu at a point where no encoder can still be recording
///     against them.
/// </summary>
/// <remarks>
///     Handles travel as <see cref="nint" /> rather than pointers because a lambda cannot capture a
///     pointer variable, and the release has to outlive the call that asked for it. See
///     <see cref="WebGpuDevice.Retire" /> for why it is deferred at all.
/// </remarks>
internal static unsafe class WgpuRelease
{
    /// <summary>
    ///     Defers replacement of a buffer-backed bind group as one ordered retirement. The bind
    ///     group is released before its buffer is destroyed, after the frame which may still have
    ///     recorded either handle has been submitted.
    /// </summary>
    internal static void DeferredBufferBinding(WebGpuDevice device, nint bindGroup, nint buffer)
    {
        if (bindGroup == 0 && buffer == 0) return;

        var api = device.Api;
        device.Retire(() =>
        {
            if (bindGroup != 0) api.BindGroupRelease((BindGroup*)bindGroup);
            if (buffer == 0) return;
            var gpuBuffer = (WgpuBuffer*)buffer;
            api.BufferDestroy(gpuBuffer);
            api.BufferRelease(gpuBuffer);
        });
    }

    /// <summary>
    ///     Defers destruction of vertex/index buffers until the frame using their previous contents
    ///     has been submitted. Replacing a chunk mesh while an encoder still references its old
    ///     buffer can otherwise make a recycled native handle point at a differently sized buffer.
    /// </summary>
    public static void DeferredBuffers(WebGpuDevice device, params nint[] buffers)
    {
        var api = device.Api;
        device.Retire(() =>
        {
            foreach (var handle in buffers)
            {
                if (handle == 0) continue;
                var buffer = (WgpuBuffer*)handle;
                api.BufferDestroy(buffer);
                api.BufferRelease(buffer);
            }
        });
    }

    /// <summary>
    ///     Releases the bind groups, then the sampler, view and texture — any of which may be zero
    ///     when the caller is only replacing part of the set.
    /// </summary>
    public static void Deferred(WebGpuDevice device, nint[] bindGroups, nint sampler, nint view, nint texture)
    {
        var api = device.Api;

        device.Retire(() =>
        {
            foreach (var bindGroup in bindGroups) api.BindGroupRelease((BindGroup*)bindGroup);

            if (sampler != 0) api.SamplerRelease((Sampler*)sampler);
            if (view != 0) api.TextureViewRelease((TextureView*)view);

            if (texture != 0)
            {
                api.TextureDestroy((Texture*)texture);
                api.TextureRelease((Texture*)texture);
            }
        });
    }
}
