using Silk.NET.WebGPU;

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
    ///     Releases the bind groups, then the sampler, view and texture — any of which may be zero
    ///     when the caller is only replacing part of the set.
    /// </summary>
    public static void Deferred(WebGpuDevice device, nint[] bindGroups, nint sampler, nint view, nint texture)
    {
        Silk.NET.WebGPU.WebGPU api = device.Api;

        device.Retire(() =>
        {
            foreach (nint bindGroup in bindGroups) api.BindGroupRelease((BindGroup*)bindGroup);

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
