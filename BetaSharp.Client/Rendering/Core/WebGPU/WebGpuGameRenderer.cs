using System.Runtime.InteropServices;
using BetaSharp.Client.Rendering.Chunks;
using BetaSharp.Client.Rendering.Entities;
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
    private WgpuPipeline? _blitPipeline;
    private WgpuMesh? _blitQuad;
    private readonly WebGpuDrawTarget _drawTarget;
    private ImGuiWgpuBackend? _imguiWgpu;
    private bool _disposed;

    public WebGpuGameRenderer(BetaSharp game)
    {
        _game = game;

        // Installed here rather than at the first frame because the sky, stars and clouds are built
        // during startup, and capturing them needs a target even though drawing them needs a pass.
        WebGpuDevice device = WebGpuDevice.Current!;
        _drawTarget = new WebGpuDrawTarget(device, device.SurfaceFormat, TextureFormat.Depth32float);
        GLManager.DrawTargetOrNull = _drawTarget;
    }

    public void RenderFrame(float tickDelta, long time)
    {
        WebGpuDevice device = WebGpuDevice.Current!;
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureView* swapView = device.AcquireFrame();
        if (swapView == null) return;

        CommandEncoder* encoder = device.CreateCommandEncoder();
        EnsureResources(device);

        // The frame that used these last has been submitted, so the buffers they hand out are free
        // again.
        _terrainPipeline!.ResetUniformPool();

        _game.GameRenderer.ProcessLookInput();

        // Null until the pack has been read and the array uploaded, which the first world load
        // does. Restated per frame because a pack switch replaces the array outright.
        WgpuTextureArray? terrain = _game.TextureManager.TerrainArray.Texture?.Wgpu;
        _drawTarget.TerrainArray = terrain;

        uint width = device.Width;
        uint height = device.Height;
        _offscreenFb!.ResizeIfNeeded(device, width, height);

        // How a block-shaped draw is lit, this frame. Default with no world, so the menus do not
        // inherit the last one's nightfall. Read by everything that builds a uniform block below.
        GLManager.WorldLight = _game.World is { } lit
            ? new WorldLightState((float)lit.Environment.AmbientDarkness, lit.Dimension.LightLevelToLuminance[0])
            : WorldLightState.Default;

        // Null in the menus, before a world is loaded — the frame still runs, so the clear, the
        // blit and the ImGui overlay are drawn; only the world is not.
        EntityLiving? camera = _game.Camera;
        WorldRenderer? world = camera is null ? null : _game.WorldRenderer;

        ChunkRenderParams chunkParams = default;
        if (camera is not null && world is not null)
        {
            chunkParams = PrepareWorldFrame(camera, world, tickDelta);
        }

        // --- Offscreen pass: terrain ---
        RenderPassEncoder* terrainPass = _offscreenFb.BeginPass(encoder,
            new Silk.NET.WebGPU.Color(0.7, 0.8, 1.0, 1.0));

        // Everything drawn through the Tessellator belongs in this pass, so the target is only open
        // for its length — a draw outside it has nowhere to go and says so.
        _drawTarget.BeginPass(terrainPass, _offscreenFb.Width, _offscreenFb.Height);

        try
        {
            // Drawing the terrain without the array would sample nothing, so the frame skips it.
            if (world != null && terrain != null)
            {
                world.ChunkRenderer.RenderSolidWebGpu(terrainPass, _terrainPipeline!, terrain);
            }
        }
        finally
        {
            _drawTarget.EndPass();
        }

        api.RenderPassEncoderEnd(terrainPass);
        api.RenderPassEncoderRelease(terrainPass);

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

        // blit.wgsl reads a quad from a vertex buffer and declares the texture and sampler in
        // group 1, behind the uniform group every pipeline here carries at group 0.
        api.RenderPassEncoderSetPipeline(swapPass, _blitPipeline!.Pipeline);
        _blitPipeline.BindUniformGroup(swapPass);
        api.RenderPassEncoderSetBindGroup(swapPass, 1,
            _offscreenFb.GetBlitBindGroup(device, _blitPipeline.TextureBindGroupLayout), 0, null);
        _blitQuad!.Draw(swapPass);

        api.RenderPassEncoderEnd(swapPass);
        api.RenderPassEncoderRelease(swapPass);

        // --- Interface pass ---
        RenderInterfacePass(device, encoder, swapView, tickDelta);

        // --- Overlay pass: ImGui ---
        // Null on any frame the game did not open and render an ImGui frame — the overlay is only
        // built when the debug windows are up.
        ImDrawDataPtr drawData = ImGui.GetDrawData();
        if (drawData.Handle is not null)
        {
            RenderPassEncoder* overlayPass = BeginSwapPass(api, encoder, swapView, null);
            _imguiWgpu!.RenderDrawData(drawData, overlayPass);
            api.RenderPassEncoderEnd(overlayPass);
            api.RenderPassEncoderRelease(overlayPass);
        }

        CommandBuffer* cmdBuf = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(device.Queue, 1, &cmdBuf);
        api.CommandBufferRelease(cmdBuf);

        // The swapchain view is an attachment of the pass just submitted, so it stays alive until
        // the submit has taken its own reference.
        api.TextureViewRelease(swapView);

        device.Present();

        // Last, because it drops the mesh buffers of chunks that fell out of range and replaces the
        // ones that were rebuilt. A buffer this frame recorded a draw from has to outlive the
        // submit — destroying it earlier fails validation inside wgpuQueueSubmit.
        if (world is not null)
        {
            world.ChunkRenderer.EndFrame(chunkParams);
        }
    }

    /// <summary>
    ///     Places the camera, works out what the frame's fog looks like, and has the chunk renderer
    ///     choose which sub-chunks are visible — everything the terrain pass reads but does not draw.
    /// </summary>
    /// <remarks>
    ///     The visibility pass is not optional: the WebGPU draw walks the set this fills, and
    ///     without it the pass records nothing at all and the world is simply absent.
    /// </remarks>
    private ChunkRenderParams PrepareWorldFrame(EntityLiving camera, WorldRenderer world, float tickDelta)
    {
        _game.GameRenderer.SetupWorldCamera(tickDelta);

        double camX = camera.LastTickX + (camera.X - camera.LastTickX) * tickDelta;
        double camY = camera.LastTickY + (camera.Y - camera.LastTickY) * tickDelta;
        double camZ = camera.LastTickZ + (camera.Z - camera.LastTickZ) * tickDelta;

        // Built before the depth remap below, because the plane extraction reads the projection and
        // expects OpenGL's symmetric clip volume.
        FrustrumCuller culler = new();
        culler.SetPosition(camX, camY, camZ);

        float viewDistance = _game.Options.RenderDistance * 16.0f;

        // No sky pass here yet, so the fog is the plain distance one and its colour is the clear.
        GLManager.Fog = GLManager.Fog with
        {
            Curve = FogCurve.Linear,
            Start = viewDistance * 0.25f,
            End = viewDistance,
            Color = new Vector4D<float>(0.7f, 0.8f, 1.0f, 1.0f),
        };

        GLManager.Projection.Load(GLManager.Projection.Top * s_glToWebGpuDepth);

        ChunkRenderParams chunkParams = new()
        {
            Camera = culler,
            ViewPos = new Vector3D<double>(camX, camY, camZ),
            RenderDistance = _game.Options.RenderDistance,
            Ticks = _game.World!.GetTime(),
            PartialTicks = tickDelta,
            DeltaTime = _game.Timer.DeltaTime,
            ChunkFade = _game.Options.ChunkFade,
            RenderOccluded = false,
        };

        world.ChunkRenderer.PrepareFrame(chunkParams);
        return chunkParams;
    }

    /// <summary>
    ///     Squashes OpenGL's -1..1 clip depth into the 0..1 WebGPU expects.
    /// </summary>
    /// <remarks>
    ///     Applied to the projection rather than fixed in the shaders because the projection is
    ///     built by the shared camera code, which the OpenGL backend uses unchanged. Right-multiplied
    ///     under the row-vector convention the matrix stack uses.
    /// </remarks>
    private static readonly Matrix4X4<float> s_glToWebGpuDepth = new(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0.5f, 0,
        0, 0, 0.5f, 1);

    /// <summary>
    ///     Draws the HUD and the current screen onto the swapchain, over the blitted world.
    /// </summary>
    /// <remarks>
    ///     A pass of its own rather than more draws in the blit's, because the interface is not all
    ///     flat: an item icon and the inventory's mob preview are 3D geometry that needs a depth
    ///     buffer, and the pipelines the draw target builds all declare one. It borrows the
    ///     offscreen framebuffer's depth texture, which the blit has finished reading from by the
    ///     time this begins, and clears it — nothing here is meant to be occluded by the world.
    /// </remarks>
    private void RenderInterfacePass(WebGpuDevice device, CommandEncoder* encoder,
        TextureView* swapView, float tickDelta)
    {
        Silk.NET.WebGPU.WebGPU api = device.Api;
        RenderPassEncoder* pass = BeginSwapPass(api, encoder, swapView, _offscreenFb!.DepthView);

        // The inventory's mob preview and the held item render through the entity dispatcher, which
        // reads the camera, the world and the font from here. WorldRenderer's frame is what normally
        // fills them in, and this renderer does not drive that frame.
        if (_game.World is not null && _game.Player is not null)
        {
            EntityRenderDispatcher.Instance.CacheRenderInfo(
                _game.World, _game.TextureManager, _game.TextRenderer, _game.Camera, _game.Options, tickDelta);
        }

        _drawTarget.BeginPass(pass, device.Width, device.Height);

        try
        {
            _game.GameRenderer.RenderInterface(tickDelta);
        }
        finally
        {
            _drawTarget.EndPass();
            api.RenderPassEncoderEnd(pass);
            api.RenderPassEncoderRelease(pass);
        }
    }

    /// <summary>
    ///     Begins a pass on the swapchain view that keeps what is already there, optionally with a
    ///     depth attachment it clears.
    /// </summary>
    private static RenderPassEncoder* BeginSwapPass(Silk.NET.WebGPU.WebGPU api, CommandEncoder* encoder,
        TextureView* swapView, TextureView* depthView)
    {
        RenderPassColorAttachment colorAttach = new()
        {
            View = swapView,
            LoadOp = LoadOp.Load,
            StoreOp = StoreOp.Store,
            DepthSlice = unchecked((uint)-1),
        };

        RenderPassDepthStencilAttachment depthAttach = new()
        {
            View = depthView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined,
        };

        RenderPassDescriptor descriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttach,
            DepthStencilAttachment = depthView is null ? null : &depthAttach,
        };

        return api.CommandEncoderBeginRenderPass(encoder, in descriptor);
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

        }

        if (_blitPipeline == null)
        {
            string blitWgsl = AssetManager.Instance.getAsset("shaders/blit.wgsl").GetTextContent();

            BindGroupLayoutEntry[] blitUniformEntries =
            [
                new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex,
                    Buffer = new BufferBindingLayout
                    {
                        Type = BufferBindingType.Uniform,
                        MinBindingSize = 64,
                    },
                },
            ];

            BindGroupLayoutEntry[] blitTextureEntries =
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
                new BindGroupLayoutEntry
                {
                    Binding = 1,
                    Visibility = ShaderStage.Fragment,
                    Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
                },
            ];

            VertexAttribute* blitAttrs = stackalloc VertexAttribute[2];
            blitAttrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
            blitAttrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 12, ShaderLocation = 1 };

            VertexBufferLayout blitLayout = new()
            {
                ArrayStride = BlitQuadStride,
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 2,
                Attributes = blitAttrs,
            };

            _blitPipeline = new WgpuPipeline(
                device, blitWgsl, "vs_main",
                uniformSize: 64,
                blitUniformEntries,
                blitTextureEntries,
                &blitLayout, 1,
                RenderState.PostProcess,
                device.SurfaceFormat);

            _blitQuad = CreateBlitQuad(device);
        }

        if (_imguiWgpu == null)
        {
            _imguiWgpu = new ImGuiWgpuBackend(device);
        }
    }

    /// <summary>Position (3 floats) then texcoord (2 floats), as blit.wgsl declares them.</summary>
    private const uint BlitQuadStride = 20;

    /// <summary>
    ///     The screen-covering quad the offscreen colour texture is sampled onto, in clip space.
    /// </summary>
    private static WgpuMesh CreateBlitQuad(WebGpuDevice device)
    {
        float[] vertices =
        [
            -1, -1, 0, 0, 1,
             1, -1, 0, 1, 1,
             1,  1, 0, 1, 0,
             1,  1, 0, 1, 0,
            -1,  1, 0, 0, 0,
            -1, -1, 0, 0, 1,
        ];

        return new WgpuMesh(device, MemoryMarshal.AsBytes(vertices.AsSpan()), BlitQuadStride);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _offscreenFb?.Dispose();
        _terrainPipeline?.Dispose();
        _blitPipeline?.Dispose();
        _blitQuad?.Dispose();
        _drawTarget.Dispose();
        _imguiWgpu?.Dispose();
    }
}
