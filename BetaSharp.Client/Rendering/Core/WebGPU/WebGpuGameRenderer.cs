using BetaSharp.Client.Rendering.Chunks;
using BetaSharp.Entities;
using Hexa.NET.ImGui;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     Orchestrates one WebGPU frame: offscreen terrain pass, blit to swapchain, ImGui overlay.
///     The caller drives it once per frame from the main game loop.
/// </summary>
public sealed unsafe class WebGpuGameRenderer : IDisposable
{
    private readonly BetaSharp _game;
    private WgpuFramebuffer? _offscreenFb;
    private WgpuPipeline? _terrainPipeline;
    private WgpuTextureArray? _terrainTextures;
    private WgpuPipeline? _blitPipeline;
    private ImGuiWgpuBackend? _imguiWgpu;
    private bool _disposed;

    public WebGpuGameRenderer(BetaSharp game)
    {
        _game = game;
    }

    public void RenderFrame(float tickDelta, long time)
    {
        WebGpuDevice device = WebGpuDevice.Current!;
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureView* swapView = device.AcquireFrame();
        if (swapView == null) return;

        CommandEncoder* encoder = device.CreateCommandEncoder();
        EnsureResources(device);

        uint width = device.Width;
        uint height = device.Height;
        _offscreenFb!.ResizeIfNeeded(device, width, height);

        // Camera position.
        Entity camera = _game.Camera;
        double camX = camera.LastTickX + (camera.X - camera.LastTickX) * tickDelta;
        double camY = camera.LastTickY + (camera.Y - camera.LastTickY) * tickDelta;
        double camZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * tickDelta;

        // Projection.
        float aspect = width / (float)height;
        float fov = _game.GameRenderer.CameraController.GetFov(tickDelta);
        float f = 1.0f / MathF.Tan(fov * MathF.PI / 360.0f);
        float viewDist = _game.Options.RenderDistance * 16.0f * 2.0f;

        Matrix4X4<float> proj = default;
        proj.M11 = f / aspect; proj.M22 = f;
        proj.M33 = viewDist / (viewDist - 0.05f); proj.M34 = 1.0f;
        proj.M43 = -(0.05f * viewDist) / (viewDist - 0.05f);

        // Model-view: translation only for now.
        Matrix4X4<float> modelView = default;
        modelView.M11 = 1.0f; modelView.M22 = 1.0f; modelView.M33 = 1.0f; modelView.M44 = 1.0f;
        modelView.M41 = -(float)camX;
        modelView.M42 = -(float)camY;
        modelView.M43 = -(float)camZ;

        ChunkUniforms uniforms = new()
        {
            ProjectionMatrix = proj,
            ModelViewMatrix = modelView,
            FogMode = 0,
            FogStart = 0,
            FogEnd = viewDist,
            FogDensity = 0,
            FogColorR = 0.7f, FogColorG = 0.8f, FogColorB = 1.0f, FogColorA = 1.0f,
            AmbientDarkness = 0,
            LuminanceOffset = 0.05f,
            ChunkFadeEnabled = 1,
            FadeProgress = 1.0f,
        };
        _terrainPipeline!.UploadUniforms(uniforms);

        // --- Offscreen pass: terrain ---
        RenderPassEncoder* terrainPass = _offscreenFb.BeginPass(encoder,
            new Silk.NET.WebGPU.Color(0.7, 0.8, 1.0, 1.0));

        WorldRenderer? world = _game.WorldRenderer;
        if (world != null)
        {
            world.ChunkRenderer.RenderSolidWebGpu(terrainPass, _terrainPipeline, _terrainTextures!);
        }

        api.RenderPassEncoderEnd(terrainPass);

        // --- Swapchain pass: blit + ImGui ---
        RenderPassColorAttachment colorAttach = new()
        {
            View = swapView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color(0, 0, 0, 1),
        };

        RenderPassDescriptor swapDesc = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttach,
        };

        RenderPassEncoder* swapPass = api.CommandEncoderBeginRenderPass(encoder, in swapDesc);

        api.RenderPassEncoderSetPipeline(swapPass, _blitPipeline!.Pipeline);
        api.RenderPassEncoderSetBindGroup(swapPass, 0,
            _offscreenFb.CreateBlitBindGroup(device, _blitPipeline.BindGroupLayout), 0, null);
        api.RenderPassEncoderDraw(swapPass, 3, 1, 0, 0);

        _imguiWgpu!.RenderDrawData(ImGui.GetDrawData(), swapPass);

        api.RenderPassEncoderEnd(swapPass);
        api.TextureViewRelease(swapView);

        CommandBuffer* cmdBuf = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(device.Queue, 1, &cmdBuf);
        api.CommandBufferRelease(cmdBuf);

        device.Present();
    }

    private void EnsureResources(WebGpuDevice device)
    {
        if (_offscreenFb == null)
        {
            _offscreenFb = WgpuFramebuffer.CreateColorDepth(device,
                device.Width, device.Height, device.SurfaceFormat);
        }

        if (_terrainPipeline == null)
        {
            string chunkWgsl = AssetManager.Instance.getAsset("shaders/chunk.wgsl").GetTextContent();

            VertexAttribute* attrs = stackalloc VertexAttribute[5];
            attrs[0] = new VertexAttribute { Format = VertexFormat.Sint16x4, Offset = 0, ShaderLocation = 0 };
            attrs[1] = new VertexAttribute { Format = VertexFormat.Uint16x2, Offset = 12, ShaderLocation = 1 };
            attrs[2] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 8, ShaderLocation = 2 };
            attrs[3] = new VertexAttribute { Format = VertexFormat.Uint8x2, Offset = 16, ShaderLocation = 3 };
            attrs[4] = new VertexAttribute { Format = VertexFormat.Uint8x2, Offset = 18, ShaderLocation = 4 };

            VertexBufferLayout bufferLayout = new()
            {
                ArrayStride = 20,
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 5,
                Attributes = attrs,
            };

            BindGroupLayoutEntry[] uniformEntries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout
                    {
                        Type = BufferBindingType.Uniform,
                        MinBindingSize = 336,
                    },
                },
            ];

            BindGroupLayoutEntry[] texEntries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout
                    {
                        SampleType = TextureSampleType.Float,
                        ViewDimension = TextureViewDimension.Dimension2DArray,
                    },
                },
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
                },
            ];

            _terrainPipeline = new WgpuPipeline(
                device, chunkWgsl, "vs_main",
                uniformSize: 336,
                uniformEntries,
                texEntries,
                &bufferLayout, 1,
                RenderState.Opaque,
                device.SurfaceFormat,
                TextureFormat.Depth32float);

            BindGroupLayout* texLayout = _terrainPipeline.TextureBindGroupLayout!;
            _terrainTextures = new WgpuTextureArray(device, 16, 256, texLayout);
        }

        if (_blitPipeline == null)
        {
            string blitWgsl = AssetManager.Instance.getAsset("shaders/blit.wgsl").GetTextContent();

            BindGroupLayoutEntry[] blitUniformEntries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout
                    {
                        SampleType = TextureSampleType.Float,
                        ViewDimension = TextureViewDimension.Dimension2D,
                    },
                },
            ];

            BindGroupLayoutEntry[] blitSamplerEntries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
                },
            ];

            _blitPipeline = new WgpuPipeline(
                device, blitWgsl, "vs_main",
                uniformSize: 64,
                blitUniformEntries,
                blitSamplerEntries,
                null, 0,
                RenderState.Opaque,
                device.SurfaceFormat);
        }

        if (_imguiWgpu == null)
        {
            _imguiWgpu = new ImGuiWgpuBackend(device);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _offscreenFb?.Dispose();
        _terrainPipeline?.Dispose();
        _terrainTextures?.Dispose();
        _blitPipeline?.Dispose();
        _imguiWgpu?.Dispose();
    }
}
