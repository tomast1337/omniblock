using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     A colour texture and an optional depth texture, owned together so a render pass that targets
///     them knows both are live.
/// </summary>
/// <remarks>
///     In the GL path these are FBO attachments. Every "FBO" the <c>FramebufferManager</c> owns
///     becomes one of these, and the manager's pass boundaries become
///     <see cref="BeginPass" />/<c>EndPass</c> pairs on the same encoder.
/// </remarks>
public sealed unsafe class WgpuFramebuffer : IDisposable
{
    public Texture* ColorTexture { get; private set; }
    public TextureView* ColorView { get; private set; }

    public Texture* DepthTexture { get; private set; }
    public TextureView* DepthView { get; private set; }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    private readonly WebGpuDevice _device;
    private readonly TextureFormat _colorFormat;
    private Sampler* _blitSampler;
    private BindGroup* _blitBindGroup;
    private bool _disposed;

    /// <summary>A format every backend offers for depth.</summary>
    public const TextureFormat DepthFormat = TextureFormat.Depth32float;

    /// <summary>Creates a colour-only framebuffer.</summary>
    public static WgpuFramebuffer CreateColor(WebGpuDevice device, uint width, uint height,
        TextureFormat format = TextureFormat.Rgba8Unorm)
    {
        CreateColorTexture(device, width, height, format,
            out Texture* cTex, out TextureView* cView);

        return new WgpuFramebuffer(device, width, height, format, cTex, cView, null, null);
    }

    /// <summary>Creates a colour + depth framebuffer.</summary>
    public static WgpuFramebuffer CreateColorDepth(WebGpuDevice device, uint width, uint height,
        TextureFormat colorFormat = TextureFormat.Rgba8Unorm)
    {
        CreateColorTexture(device, width, height, colorFormat,
            out Texture* cTex, out TextureView* cView);

        CreateDepthTexture(device, width, height,
            out Texture* dTex, out TextureView* dView);

        return new WgpuFramebuffer(device, width, height, colorFormat, cTex, cView, dTex, dView);
    }

    private WgpuFramebuffer(WebGpuDevice device, uint width, uint height, TextureFormat colorFormat,
        Texture* colorTex, TextureView* colorView,
        Texture* depthTex, TextureView* depthView)
    {
        _device = device;
        _colorFormat = colorFormat;
        Width = width;
        Height = height;
        ColorTexture = colorTex;
        ColorView = colorView;
        DepthTexture = depthTex;
        DepthView = depthView;
    }

    /// <summary>
    ///     Begins a render pass targeting this framebuffer and returns the pass encoder. The caller
    ///     ends and releases it.
    /// </summary>
    public RenderPassEncoder* BeginPass(CommandEncoder* encoder,
        Silk.NET.WebGPU.Color clearColor, bool clearColorBuffer = true,
        bool clearDepth = true)
    {
        Silk.NET.WebGPU.WebGPU api = _device.Api;

        RenderPassColorAttachment colorAttach = new()
        {
            View = ColorView,
            LoadOp = clearColorBuffer ? LoadOp.Clear : LoadOp.Load,
            StoreOp = StoreOp.Store,
            ClearValue = clearColor,
            DepthSlice = unchecked((uint)-1),
        };

        RenderPassDepthStencilAttachment depthAttach = default;
        RenderPassDepthStencilAttachment* pDepth = null;

        if (DepthView is not null)
        {
            depthAttach = new RenderPassDepthStencilAttachment
            {
                View = DepthView,
                DepthLoadOp = clearDepth ? LoadOp.Clear : LoadOp.Load,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
            };

            pDepth = &depthAttach;
        }

        RenderPassDescriptor descriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttach,
            DepthStencilAttachment = pDepth,
        };

        return api.CommandEncoderBeginRenderPass(encoder, in descriptor);
    }

    /// <summary>
    ///     Recreates the colour and depth textures when the size changes. No-op when sizes match.
    /// </summary>
    public void ResizeIfNeeded(WebGpuDevice device, uint width, uint height)
    {
        if (width == Width && height == Height) return;

        // Release old textures.
        Silk.NET.WebGPU.WebGPU api = device.Api;
        if (ColorView is not null) api.TextureViewRelease(ColorView);
        if (DepthView is not null) api.TextureViewRelease(DepthView);
        if (ColorTexture is not null) { api.TextureDestroy(ColorTexture); api.TextureRelease(ColorTexture); }
        if (DepthTexture is not null) { api.TextureDestroy(DepthTexture); api.TextureRelease(DepthTexture); }

        // The pipelines that target this were built against the format it was created with, so a
        // resize keeps it. Recreating as Rgba8Unorm silently invalidated every one of them.
        Width = width;
        Height = height;

        ReleaseBlitBindGroup();

        CreateColorTexture(device, width, height, _colorFormat,
            out Texture* cTex, out TextureView* cView);
        CreateDepthTexture(device, width, height,
            out Texture* dTex, out TextureView* dView);

        ColorTexture = cTex;
        ColorView = cView;
        DepthTexture = dTex;
        DepthView = dView;
    }

    /// <summary>
    ///     The bind group a blit shader samples the colour texture through, built once and kept
    ///     until a resize replaces the texture it points at.
    /// </summary>
    /// <remarks>
    ///     <paramref name="layout" /> must be the layout of the group the shader declares the
    ///     texture and sampler in — group 1 in the pipelines built here, not the uniform group.
    /// </remarks>
    public BindGroup* GetBlitBindGroup(WebGpuDevice device, BindGroupLayout* layout)
    {
        if (_blitBindGroup is not null) return _blitBindGroup;

        Silk.NET.WebGPU.WebGPU api = device.Api;

        if (_blitSampler is null)
        {
            SamplerDescriptor samplerDesc = new()
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Nearest,
                LodMinClamp = 0.0f,
                LodMaxClamp = 1.0f,
                MaxAnisotropy = 1,
            };

            _blitSampler = api.DeviceCreateSampler(device.Device, in samplerDesc);
        }

        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = ColorView };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _blitSampler };

        BindGroupDescriptor desc = new()
        {
            Layout = layout,
            EntryCount = 2,
            Entries = entries,
        };

        _blitBindGroup = api.DeviceCreateBindGroup(device.Device, in desc);
        return _blitBindGroup;
    }

    private void ReleaseBlitBindGroup()
    {
        if (_blitBindGroup is null) return;

        _device.Api.BindGroupRelease(_blitBindGroup);
        _blitBindGroup = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        ReleaseBlitBindGroup();
        if (_blitSampler is not null) { api.SamplerRelease(_blitSampler); _blitSampler = null; }
        if (ColorView is not null) api.TextureViewRelease(ColorView);
        if (DepthView is not null) api.TextureViewRelease(DepthView);
        if (ColorTexture is not null) { api.TextureDestroy(ColorTexture); api.TextureRelease(ColorTexture); }
        if (DepthTexture is not null) { api.TextureDestroy(DepthTexture); api.TextureRelease(DepthTexture); }
    }

    private static void CreateColorTexture(WebGpuDevice device, uint width, uint height,
        TextureFormat format, out Texture* tex, out TextureView* view)
    {
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureDescriptor desc = new()
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = format,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        tex = api.DeviceCreateTexture(device.Device, in desc);

        TextureViewDescriptor viewDesc = new()
        {
            Format = format,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        view = api.TextureCreateView(tex, in viewDesc);
    }

    private static void CreateDepthTexture(WebGpuDevice device, uint width, uint height,
        out Texture* tex, out TextureView* view)
    {
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureDescriptor desc = new()
        {
            Usage = TextureUsage.RenderAttachment,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = DepthFormat,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        tex = api.DeviceCreateTexture(device.Device, in desc);

        TextureViewDescriptor viewDesc = new()
        {
            Format = DepthFormat,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.DepthOnly,
        };

        view = api.TextureCreateView(tex, in viewDesc);
    }
}
