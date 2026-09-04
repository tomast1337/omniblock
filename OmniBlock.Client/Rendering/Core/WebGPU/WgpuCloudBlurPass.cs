using System.Numerics;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     WebGPU port of <c>FramebufferManager.BeginCloudPass</c>/<c>EndCloudPass</c>: captures
///     whatever <c>RenderClouds</c> draws between <see cref="Begin" /> and <see cref="End" /> into
///     its own colour+depth target (depth pre-populated from the world pass so clouds are occluded
///     by terrain already drawn), runs a separable Gaussian blur through <c>cloud_blur.wgsl</c>, and
///     composites the result back onto the world framebuffer with depth testing so nearer terrain
///     still occludes it.
/// </summary>
/// <remarks>
///     Owned and resized by <see cref="WebGpuGameRenderer" /> in lockstep with its own offscreen
///     framebuffer. <see cref="Encoder" /> must be set every frame, before <c>DrawWorld</c> runs,
///     since <see cref="GameRenderer.DrawWorld" /> reaches this through the parameterless
///     <see cref="ICloudBlurPass" /> interface and has no encoder of its own to hand in.
/// </remarks>
public sealed unsafe class WgpuCloudBlurPass : ICloudBlurPass, IDisposable
{
    private const uint QuadStride = 20;
    private readonly WgpuFramebuffer _cloudFb;

    private readonly WebGpuDevice _device;
    private readonly WebGpuDrawTarget _drawTarget;
    private readonly WgpuPipeline _horizontalPipeline;
    private readonly WgpuFramebuffer _offscreenFb;
    private readonly WgpuFramebuffer _pingPongFb;
    private readonly WgpuMesh _quad;
    private readonly WgpuPipeline _verticalPipeline;
    private RenderPassEncoder* _cloudPass;
    private bool _disposed;

    public WgpuCloudBlurPass(WebGpuDevice device, WebGpuDrawTarget drawTarget,
        WgpuFramebuffer offscreenFb, WgpuMesh quad)
    {
        _device = device;
        _drawTarget = drawTarget;
        _offscreenFb = offscreenFb;
        _quad = quad;

        // Same colour format as the offscreen framebuffer, not the default: RenderClouds draws into
        // this through the same cloud pipeline it always uses, which was built matching the
        // offscreen framebuffer's actual format — Bgra8Unorm on most Vulkan surfaces, not the
        // Rgba8Unorm CreateColorDepth defaults to. A mismatch here is a validation error, not a
        // colour bug: wgpu rejects setting a pipeline on a pass whose target format it wasn't built
        // for. CopyDst so Begin() can seed this from the offscreen depth via a texture-to-texture
        // copy; the offscreen framebuffer's own depth texture needs the matching CopySrc, set where
        // WebGpuGameRenderer creates it.
        _cloudFb = WgpuFramebuffer.CreateColorDepth(device, offscreenFb.Width, offscreenFb.Height,
            device.SurfaceFormat, TextureUsage.CopyDst);

        // Colour-only scratch buffer for the horizontal pass: nothing outside this class ever draws
        // into it, so it does not need to match anything and stays the default format.
        _pingPongFb = WgpuFramebuffer.CreateColor(device, offscreenFb.Width, offscreenFb.Height);

        var wgsl = AssetManager.Instance.GetAsset("shaders/cloud_blur.wgsl").GetTextContent();

        BindGroupLayoutEntry[] uniformEntries =
        [
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    MinBindingSize = 4
                }
            }
        ];

        BindGroupLayoutEntry[] textureEntries =
        [
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D
                }
            },
            new()
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            }
        ];

        var attrs = stackalloc VertexAttribute[2];
        attrs[0] = new VertexAttribute
        {
            Format = VertexFormat.Float32x3,
            Offset = 0,
            ShaderLocation = 0
        };
        attrs[1] = new VertexAttribute
        {
            Format = VertexFormat.Float32x2,
            Offset = 12,
            ShaderLocation = 1
        };

        VertexBufferLayout quadLayout = new()
        {
            ArrayStride = QuadStride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 2,
            Attributes = attrs
        };

        // Colour-only, no depth attachment: the horizontal pass just accumulates into a scratch
        // buffer, nothing occludes it.
        _horizontalPipeline = new WgpuPipeline(
            device, wgsl, "vs_main", 4,
            uniformEntries, textureEntries, &quadLayout, 1,
            RenderState.PostProcess, TextureFormat.Rgba8Unorm,
            label: "CloudBlur.Horizontal");

        // Targets the offscreen framebuffer itself, alpha-blended and depth tested against what
        // DrawWorld already drew, so terrain in front of a distant cloud still occludes its glow.
        _verticalPipeline = new WgpuPipeline(
            device, wgsl, "vs_main", 4,
            uniformEntries, textureEntries, &quadLayout, 1,
            RenderState.PostProcess with
            {
                Blend = BlendMode.Alpha,
                DepthTest = true
            },
            device.SurfaceFormat, WgpuFramebuffer.DepthFormat,
            label: "CloudBlur.Vertical");
    }

    /// <summary>
    ///     The command encoder the currently open frame is recording into. Set by
    ///     <see cref="WebGpuGameRenderer" /> once per frame, before <c>DrawWorld</c> runs.
    /// </summary>
    public CommandEncoder* Encoder { get; set; }

    /// <summary>
    ///     The current fog/sky colour, RGB only — alpha is forced to 0 when this is used to clear
    ///     <see cref="_cloudFb" />. Set by <see cref="WebGpuGameRenderer" /> once per frame, mirroring
    ///     GL's <c>ClearColor(fogR, fogG, fogB, 0)</c> call that <c>FramebufferManager.BeginCloudPass</c>
    ///     inherits (<c>GameRenderer.cs</c>'s fog-colour update, ~line 1052). It has to be the sky
    ///     colour and not black: the blur samples with linear filtering, which blends RGB and alpha
    ///     independently, so a fully-transparent texel's stored RGB still bleeds into a bilinearly
    ///     filtered sample right at a cloud's silhouette edge even though the shader premultiplies by
    ///     alpha afterwards. A black background leaks black into that edge; the sky colour leaks the
    ///     colour a fading cloud edge is supposed to blend into.
    /// </summary>
    public Vector3 FogColor { get; set; }

    public void Begin()
    {
        var api = _device.Api;

        // Ends whatever pass DrawWorld's earlier draws landed in — this is always the world pass
        // itself, opened by WebGpuGameRenderer.RenderFrame just before DrawWorld ran, since nothing
        // else in DrawWorld brackets a pass boundary before the cloud draw.
        var worldPass = _drawTarget.CurrentPass;
        _drawTarget.EndPass();
        api.RenderPassEncoderEnd(worldPass);
        api.RenderPassEncoderRelease(worldPass);

        ImageCopyTexture copySrc = new()
        {
            Texture = _offscreenFb.DepthTexture,
            MipLevel = 0,
            Origin = default,
            Aspect = TextureAspect.DepthOnly
        };
        ImageCopyTexture copyDst = new()
        {
            Texture = _cloudFb.DepthTexture,
            MipLevel = 0,
            Origin = default,
            Aspect = TextureAspect.DepthOnly
        };
        Extent3D copySize = new(_offscreenFb.Width, _offscreenFb.Height, 1);
        api.CommandEncoderCopyTextureToTexture(Encoder, in copySrc, in copyDst, in copySize);

        _cloudPass = _cloudFb.BeginPass(Encoder,
            new Color(FogColor.X, FogColor.Y, FogColor.Z, 0.0),
            true, false);
        _drawTarget.BeginPass(_cloudPass, _cloudFb.Width, _cloudFb.Height);
    }

    public void End()
    {
        var api = _device.Api;

        _drawTarget.EndPass();
        api.RenderPassEncoderEnd(_cloudPass);
        api.RenderPassEncoderRelease(_cloudPass);
        _cloudPass = null;

        // Horizontal blur: cloud capture -> ping-pong, premultiplying alpha as it accumulates.
        var hPass = _pingPongFb.BeginPass(Encoder, default);
        api.RenderPassEncoderSetPipeline(hPass, _horizontalPipeline.Pipeline);
        _horizontalPipeline.UploadUniforms(1u);
        _horizontalPipeline.BindUniformGroup(hPass);
        api.RenderPassEncoderSetBindGroup(hPass, 1,
            _cloudFb.GetBlitBindGroup(_device, _horizontalPipeline.TextureBindGroupLayout), 0, null);
        _quad.Draw(hPass);
        api.RenderPassEncoderEnd(hPass);
        api.RenderPassEncoderRelease(hPass);

        // Vertical blur, composited straight onto the world framebuffer: LoadOp.Load on both colour
        // and depth so everything DrawWorld drew before clouds survives, and the scene's own depth
        // decides how much of the glow shows through in front of nearer terrain.
        var vPass = _offscreenFb.BeginPass(Encoder, default,
            false, false);
        api.RenderPassEncoderSetPipeline(vPass, _verticalPipeline.Pipeline);
        _verticalPipeline.UploadUniforms(0u);
        _verticalPipeline.BindUniformGroup(vPass);
        api.RenderPassEncoderSetBindGroup(vPass, 1,
            _pingPongFb.GetBlitBindGroup(_device, _verticalPipeline.TextureBindGroupLayout), 0, null);
        _quad.Draw(vPass);

        // Left open on purpose: DrawWorld's remaining draws, and WebGpuGameRenderer's own
        // end/release once DrawWorld returns, continue against this pass — see
        // WebGpuDrawTarget.CurrentPass, which is how the caller finds it again instead of the stale
        // pass pointer it opened the frame with.
        _drawTarget.BeginPass(vPass, _offscreenFb.Width, _offscreenFb.Height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cloudFb.Dispose();
        _pingPongFb.Dispose();
        _horizontalPipeline.Dispose();
        _verticalPipeline.Dispose();
    }

    /// <summary>Resizes the capture buffers in lockstep with the offscreen framebuffer.</summary>
    public void Resize(WebGpuDevice device, uint width, uint height)
    {
        _cloudFb.ResizeIfNeeded(device, width, height);
        _pingPongFb.ResizeIfNeeded(device, width, height);
    }
}
