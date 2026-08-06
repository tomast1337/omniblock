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
    private bool _disposed;

    /// <summary>A format every backend offers for depth.</summary>
    public const TextureFormat DepthFormat = TextureFormat.Depth32float;

    /// <summary>Creates a colour-only framebuffer.</summary>
    public static WgpuFramebuffer CreateColor(WebGpuDevice device, uint width, uint height,
        TextureFormat format = TextureFormat.Rgba8Unorm)
    {
        CreateColorTexture(device, width, height, format,
            out Texture* cTex, out TextureView* cView);

        return new WgpuFramebuffer(device, width, height, cTex, cView, null, null);
    }

    /// <summary>Creates a colour + depth framebuffer.</summary>
    public static WgpuFramebuffer CreateColorDepth(WebGpuDevice device, uint width, uint height,
        TextureFormat colorFormat = TextureFormat.Rgba8Unorm)
    {
        CreateColorTexture(device, width, height, colorFormat,
            out Texture* cTex, out TextureView* cView);

        CreateDepthTexture(device, width, height,
            out Texture* dTex, out TextureView* dView);

        return new WgpuFramebuffer(device, width, height, cTex, cView, dTex, dView);
    }

    private WgpuFramebuffer(WebGpuDevice device, uint width, uint height,
        Texture* colorTex, TextureView* colorView,
        Texture* depthTex, TextureView* depthView)
    {
        _device = device;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
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
