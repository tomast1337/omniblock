using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Entities;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     Orchestrates one WebGPU frame: offscreen terrain pass, blit to swapchain, ImGui overlay.
///     The caller drives it once per frame from the main game loop.
/// </summary>
public sealed unsafe class WebGpuGameRenderer : IDisposable
{
    /// <summary>Position (3 floats) then texcoord (2 floats), as blit.wgsl declares them.</summary>
    private const uint BlitQuadStride = 20;

    private readonly WebGpuDrawTarget _drawTarget;
    private readonly OmniBlock _game;
    private readonly Dictionary<Texture2D, (WgpuTexture Texture, ulong ImGuiId)> _imguiTextures = [];
    private WgpuPipeline? _blitPipeline;
    private WgpuMesh? _blitQuad;
    private WgpuCloudBlurPass? _cloudBlurPass;
    private bool _disposed;
    private ImGuiWgpuBackend? _imguiWgpu;
    private WgpuFramebuffer? _offscreenFb;
    private WgpuFramebuffer? _presentFb;

    public WebGpuGameRenderer(OmniBlock game)
    {
        _game = game;

        // Installed here rather than at the first frame because the sky, stars and clouds are built
        // during startup, and capturing them needs a target even though drawing them needs a pass.
        var device = WebGpuDevice.Current!;
        _drawTarget = new WebGpuDrawTarget(device, device.SurfaceFormat, TextureFormat.Depth32float);
        RenderSystem.DrawTargetOrNull = _drawTarget;
    }

    /// <summary>
    ///     Set by the caller from the F3 debug viewport's content size, in pixels; null when that
    ///     window is not open. While set, the world renders at this size into the offscreen
    ///     framebuffer instead of the swapchain's, and the swapchain is left cleared rather than
    ///     blitted onto — see <see cref="ViewportTextureId" />.
    /// </summary>
    public (uint Width, uint Height)? ViewportSize { get; set; }

    /// <summary>
    ///     Id ImGui.Image can draw the world's last frame through while <see cref="ViewportSize" />
    ///     is set. Zero otherwise.
    /// </summary>
    public ulong ViewportTextureId { get; private set; }

    /// <summary>
    ///     Set by the caller to whether the debug UI is open this frame. ImGui only calls
    ///     <c>ImGui.Render()</c> on such a frame — on every other one, <c>ImGui.GetDrawData()</c>
    ///     keeps returning whatever it last built, so without this the overlay pass would go on
    ///     redrawing a stale, fully-open debug UI (opaque docked panel backgrounds and all) forever
    ///     after the UI was closed, with only its Game Viewport image blanked out from the texture
    ///     this frame just unregistered — which is what painted the screen solid dark, not black
    ///     exactly, but close enough to read as it.
    /// </summary>
    public bool ImguiOpen { get; set; }

    /// <summary>
    ///     Set by the caller (<c>OmniBlock.ScreenshotListener</c>) to have the next
    ///     <see cref="RenderFrame" /> copy out and save the composited, gamma-corrected frame. One
    ///     frame behind the key press rather than the same frame — the key is polled after
    ///     RenderFrame has already run for the frame that press lands on — which nothing can
    ///     perceive in a screenshot.
    /// </summary>
    public bool ScreenshotRequested { get; set; }

    /// <summary>
    ///     The message from the most recently finished capture, consumed and cleared by the caller.
    ///     GL's <c>ScreenShotHelper.saveScreenshot</c> returns this synchronously; WebGPU cannot —
    ///     the GPU readback the capture needs only finishes partway through the RenderFrame call
    ///     after the one that set <see cref="ScreenshotRequested" />, so the caller has to poll.
    /// </summary>
    public string? ScreenshotResult { get; set; }

    /// <summary>
    ///     Pixel size of the framebuffer the world/HUD pass is currently drawing into: the window's
    ///     size normally, or the smaller F3 viewport's size while it is open. Mirrors GL's
    ///     <c>FramebufferManager.FramebufferWidth/Height</c> so <c>UIContext.RenderTargetSize</c> can
    ///     report the right scale for scissor rects (<see cref="UI.Rendering.UIRenderer.EnableClipping" />)
    ///     under either backend — before the first frame runs this is (0, 0), which the caller falls
    ///     back away from.
    /// </summary>
    public (uint Width, uint Height) FramebufferSize => (_offscreenFb?.Width ?? 0, _offscreenFb?.Height ?? 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _offscreenFb?.Dispose();
        _presentFb?.Dispose();
        _blitPipeline?.Dispose();
        _blitQuad?.Dispose();
        _cloudBlurPass?.Dispose();
        _drawTarget.Dispose();
        _imguiWgpu?.Dispose();
    }

    /// <summary>
    ///     Returns an ImGui texture id for an engine texture. <see cref="Texture2D.Id" /> is only a
    ///     renderer batching key and must never be passed to ImGui directly.
    /// </summary>
    internal ulong GetImGuiTextureId(TextureHandle handle)
    {
        var texture = handle.Texture?.Wgpu;
        if (texture is null || _imguiWgpu is null) return 0;

        if (_imguiTextures.TryGetValue(handle.Texture!, out var registered))
        {
            if (!ReferenceEquals(registered.Texture, texture))
            {
                _imguiWgpu.UpdateExternalTexture(registered.ImGuiId, texture.View);
                _imguiTextures[handle.Texture!] = (texture, registered.ImGuiId);
            }

            return registered.ImGuiId;
        }

        var id = _imguiWgpu.RegisterExternalTexture(texture.View);
        _imguiTextures.Add(handle.Texture!, (texture, id));
        return id;
    }

    public void RenderFrame(float tickDelta, long time)
    {
        var device = WebGpuDevice.Current!;
        var api = device.Api;

        var swapView = device.AcquireFrame();
        if (swapView == null) return;

        var encoder = device.CreateCommandEncoder();
        EnsureResources(device);

        // Every pass below records into that one encoder and is submitted together, which is what
        // makes this the point the target's buffers become free again rather than each BeginPass.
        _drawTarget.BeginFrame();

        _game.GameRenderer.ProcessLookInput();

        // Null until the pack has been read and the array uploaded, which the first world load
        // does. Restated per frame because a pack switch replaces the array outright.
        var terrain = _game.TextureManager.TerrainArray.Texture?.Wgpu;
        _drawTarget.TerrainArray = terrain;

        (uint Width, uint Height)? viewport = ViewportSize is { Width: > 0, Height: > 0 } vp ? vp : null;
        var width = viewport?.Width ?? device.Width;
        var height = viewport?.Height ?? device.Height;

        var resized = _offscreenFb!.ResizeIfNeeded(device, width, height);
        _presentFb!.ResizeIfNeeded(device, width, height);
        _cloudBlurPass!.Resize(device, width, height);
        _cloudBlurPass.Encoder = encoder;

        if (viewport is not null)
        {
            // Register once, then Update (never re-Register) on every real resize after that — the
            // id has to stay stable across a resize: see ImGuiWgpuBackend.UpdateExternalTexture for
            // why minting a fresh id here silently blanked the image forever instead of just on the
            // frames an actual resize happened.
            //
            // This has to key off _offscreenFb's ResizeIfNeeded return value, not compare ColorView
            // against a remembered pointer: a released TextureView's address can be handed back by
            // the allocator to a later, genuinely-different view, so a pointer that looks unchanged
            // is not proof the view is — that false "unchanged" reading is what let a stale bind
            // group survive a real resize and made the viewport image freeze on whatever it last
            // held. _presentFb resizes in lockstep with _offscreenFb (same width/height, above), so
            // one flag covers both.
            if (ViewportTextureId == 0)
            {
                ViewportTextureId = _imguiWgpu!.RegisterExternalTexture(_presentFb.ColorView);
            }
            else if (resized)
            {
                _imguiWgpu!.UpdateExternalTexture(ViewportTextureId, _presentFb.ColorView);
            }
        }
        else if (ViewportTextureId != 0)
        {
            _imguiWgpu?.UnregisterExternalTexture(ViewportTextureId);
            ViewportTextureId = 0;
        }

        // How a block-shaped draw is lit, this frame. Default with no world, so the menus do not
        // inherit the last one's nightfall. Read by everything that builds a uniform block below.
        RenderSystem.WorldLight = _game.World is { } lit
            ? new WorldLightState(lit.Environment.AmbientDarkness, lit.Dimension.LightLevelToLuminance[0])
            : WorldLightState.Default;

        // Null in the menus, before a world is loaded — the frame still runs, so the clear, the
        // blit and the ImGui overlay are drawn; only the world is not. Drawing the world without
        // the terrain array would sample nothing, so that is waited on too.
        var drawWorld = _game.World is not null && _game.Camera is not null && terrain is not null;

        // Settled before the pass opens, because WebGPU clears as part of beginning one rather than
        // with a call inside it.
        Color clear = new(0.7, 0.8, 1.0, 1.0);
        if (drawWorld)
        {
            _game.GameRenderer.BeginWorldFrame(tickDelta, _game.World!.GetTime());
            var fog = _game.GameRenderer.WorldClearColor;
            clear = new Color(fog.X, fog.Y, fog.Z, 1.0);
            _cloudBlurPass!.FogColor = new Vector3(fog.X, fog.Y, fog.Z);
        }

        // Isolated offscreen capture must finish before opening any world render pass.
        if (drawWorld) _game.WorldRenderer.EntityImpostors.Prepare(device, encoder, _game.TextureManager);

        // --- Offscreen pass: the world ---
        var worldPass = _offscreenFb.BeginPass(encoder, clear);

        // Everything drawn through the Tessellator belongs in this pass, so the target is only open
        // for its length — a draw outside it has nowhere to go and says so. The chunk meshes record
        // their own draws and take the pass from the target rather than being handed it.
        _drawTarget.BeginPass(worldPass, _offscreenFb.Width, _offscreenFb.Height);

        try
        {
            if (drawWorld)
            {
                // The hand is drawn in a pass of its own below, not here — see RenderFirstPersonHand.
                _game.GameRenderer.DrawWorld(tickDelta, false);
            }
        }
        finally
        {
            // Not necessarily the pass opened above: the Soft Clouds bracket inside DrawWorld
            // (RenderSystem.CloudBlurPassOrNull) ends that pass and reopens a new one on the same
            // attachments while capturing and compositing the blur, and _drawTarget.CurrentPass is
            // how the replacement is found here instead of ending/releasing the stale local.
            worldPass = _drawTarget.CurrentPass;
            _drawTarget.EndPass();
        }

        api.RenderPassEncoderEnd(worldPass);
        api.RenderPassEncoderRelease(worldPass);

        // --- Offscreen pass 2: the first-person hand ---
        if (drawWorld)
        {
            RenderFirstPersonHand(_offscreenFb, encoder, tickDelta);
        }

        // --- Interface pass ---
        // Always composited into the offscreen framebuffer now, world and HUD together, so the
        // single gamma pass below corrects both at once — matching FramebufferManager's single
        // end-of-frame gamma pass under GL, which gamma-corrects everything in its one FBO,
        // including the HUD, not just the world.
        RenderInterfacePass(device, encoder, tickDelta);

        // --- Swapchain pass: gamma-correct the composited frame onto its final target(s) ---
        // gamma.frag's GL equivalent is a full-screen pass FramebufferManager.End runs every frame
        // regardless of the debug viewport, so this runs the same way here: blit.wgsl now applies
        // the same gamma curve, and every consumer of the composited frame reads it through this,
        // never straight off _offscreenFb.
        // While the F3 debug viewport is open the frame belongs in its ImGui::Image (drawn into
        // _presentFb below), not blitted full-screen underneath it — leaving the swapchain cleared
        // is enough; the compositor still needs *something* to present.
        if (viewport is null)
        {
            BlitToSwapchain(device, encoder, swapView);
        }
        else
        {
            RenderPassColorAttachment colorAttach = new()
            {
                View = swapView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0, 0, 0, 1)
            };
            RenderPassDescriptor swapDesc = new()
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttach
            };
            var swapPass = api.CommandEncoderBeginRenderPass(encoder, in swapDesc);
            api.RenderPassEncoderEnd(swapPass);
            api.RenderPassEncoderRelease(swapPass);
        }

        // --- Present pass: the same gamma-corrected frame again, into the texture ImGui shows
        // while the F3 debug viewport is open, or a screenshot capture reads from — both need a
        // texture created with CopySrc, which the swapchain's own is not guaranteed to carry.
        var capturing = ScreenshotRequested;
        if (viewport is not null || capturing)
        {
            var presentPass = BeginSwapPass(api, encoder, _presentFb!.ColorView, null);
            api.RenderPassEncoderSetPipeline(presentPass, _blitPipeline!.Pipeline);
            _blitPipeline.BindUniformGroup(presentPass);
            api.RenderPassEncoderSetBindGroup(presentPass, 1,
                _offscreenFb!.GetBlitBindGroup(device, _blitPipeline.TextureBindGroupLayout), 0, null);
            _blitQuad!.Draw(presentPass);
            api.RenderPassEncoderEnd(presentPass);
            api.RenderPassEncoderRelease(presentPass);
        }

        // --- Overlay pass: ImGui ---
        // The caller calls ImGui.Render() before RenderFrame() for exactly this reason: ImGui
        // invalidates GetDrawData() the moment NewFrame() starts the next frame, and only this
        // frame's Render() call — which the caller has to have already made by the time execution
        // reaches here — makes it valid again. ImguiOpen is still needed on top of the null check:
        // GetDrawData() keeps returning the last-built draw data on every frame the debug UI is
        // closed, since NewFrame()/Render() are not called at all while it is, and drawing that
        // forever after close is exactly the bug this guards against.
        var drawData = ImGui.GetDrawData();
        if (ImguiOpen && drawData.Handle is not null)
        {
            var overlayPass = BeginSwapPass(api, encoder, swapView, null);
            _imguiWgpu!.RenderDrawData(drawData, overlayPass);
            api.RenderPassEncoderEnd(overlayPass);
            api.RenderPassEncoderRelease(overlayPass);
        }

        // A screenshot copies out of _presentFb — populated above precisely because capturing was
        // true — into a MapRead buffer. The copy has to be recorded on this same encoder, before
        // it is finished; the actual host-side read happens after submission, once the GPU has run
        // it (see the map/poll loop below Present()).
        WgpuBuffer* screenshotBuffer = null;
        uint screenshotBytesPerRow = 0, screenshotWidth = 0, screenshotHeight = 0;
        if (capturing)
        {
            screenshotWidth = _presentFb!.Width;
            screenshotHeight = _presentFb.Height;
            screenshotBytesPerRow = (screenshotWidth * 4 + 255) / 256 * 256;

            BufferDescriptor screenshotBufferDesc = new()
            {
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead,
                Size = screenshotBytesPerRow * screenshotHeight
            };
            screenshotBuffer = api.DeviceCreateBuffer(device.Device, in screenshotBufferDesc);

            ImageCopyTexture copySrc = new()
            {
                Texture = _presentFb.ColorTexture,
                MipLevel = 0,
                Origin = default,
                Aspect = TextureAspect.All
            };
            ImageCopyBuffer copyDst = new()
            {
                Buffer = screenshotBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = screenshotBytesPerRow,
                    RowsPerImage = screenshotHeight
                }
            };
            Extent3D copySize = new(screenshotWidth, screenshotHeight, 1);
            api.CommandEncoderCopyTextureToBuffer(encoder, in copySrc, in copyDst, in copySize);
        }

        var cmdBuf = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(device.Queue, 1, &cmdBuf);
        api.CommandBufferRelease(cmdBuf);

        // The swapchain view is an attachment of the pass just submitted, so it stays alive until
        // the submit has taken its own reference.
        api.TextureViewRelease(swapView);

        device.Present();

        if (capturing)
        {
            ScreenshotResult = ReadBackScreenshot(
                device, screenshotBuffer, screenshotWidth, screenshotHeight, screenshotBytesPerRow);
            api.BufferRelease(screenshotBuffer);
            ScreenshotRequested = false;
        }

        // Last, because it drops the mesh buffers of chunks that fell out of range and replaces the
        // ones that were rebuilt. A buffer this frame recorded a draw from has to outlive the
        // submit — destroying it earlier fails validation inside wgpuQueueSubmit, which is why the
        // GL path can do this inside its terrain draw and this one cannot.
        if (drawWorld)
        {
            _game.WorldRenderer.ChunkRenderer.EndFrame();
        }
    }

    /// <summary>
    ///     Draws and presents one frame outside the main game loop: the boot splash and the in-world
    ///     loading screen both need to show progress on screen before (or between) real frames exist.
    /// </summary>
    /// <remarks>
    ///     A stripped-down <see cref="RenderFrame" />: acquire, open a pass on the offscreen
    ///     framebuffer for <paramref name="draw" /> to record into, then blit it to the swapchain
    ///     through the same gamma-correcting pipeline every real frame uses. That last part is a
    ///     small, harmless behaviour change from the old GL splash, which drew straight to the
    ///     backbuffer with no gamma correction at all — this one now reads the gamma slider like
    ///     everything else does. Runs through the same <see cref="EnsureResources" /> as
    ///     <see cref="RenderFrame" /> — safe because <see cref="OmniBlock.StartGame" /> now calls
    ///     <c>SetupRenderingAndInput</c> (where <c>ImGui.CreateContext()</c> runs) before
    ///     <c>LoadScreen</c>, so the ImGui backend this pulls in already has a context to attach to.
    ///     Also replays the last ImGui draw data the same way <see cref="RenderFrame" /> does, so an
    ///     already-open F3 overlay stays on screen — non-interactively, since nothing pumps a fresh
    ///     ImGui frame while loading blocks the main loop — instead of disappearing for the load.
    /// </remarks>
    public void RenderLoadingFrame(Action draw)
    {
        var device = WebGpuDevice.Current!;
        var api = device.Api;

        var swapView = device.AcquireFrame();
        if (swapView == null) return;

        var encoder = device.CreateCommandEncoder();
        EnsureResources(device);

        _drawTarget.BeginFrame();
        _offscreenFb!.ResizeIfNeeded(device, device.Width, device.Height);

        var pass = _offscreenFb.BeginPass(encoder, new Color(0, 0, 0, 1));
        _drawTarget.BeginPass(pass, _offscreenFb.Width, _offscreenFb.Height);
        RenderSystem.State.Apply(RenderState.Interface);

        try
        {
            draw();
        }
        finally
        {
            _drawTarget.EndPass();
            api.RenderPassEncoderEnd(pass);
            api.RenderPassEncoderRelease(pass);
        }

        BlitToSwapchain(device, encoder, swapView);

        var drawData = ImGui.GetDrawData();
        if (ImguiOpen && drawData.Handle is not null)
        {
            var overlayPass = BeginSwapPass(api, encoder, swapView, null);
            _imguiWgpu!.RenderDrawData(drawData, overlayPass);
            api.RenderPassEncoderEnd(overlayPass);
            api.RenderPassEncoderRelease(overlayPass);
        }

        var cmdBuf = api.CommandEncoderFinish(encoder, null);
        api.QueueSubmit(device.Queue, 1, &cmdBuf);
        api.CommandBufferRelease(cmdBuf);

        api.TextureViewRelease(swapView);
        device.Present();
    }

    /// <summary>
    ///     Gamma-corrects <see cref="_offscreenFb" /> onto <paramref name="swapView" /> through
    ///     <see cref="_blitPipeline" />. Shared by <see cref="RenderFrame" /> and
    ///     <see cref="RenderLoadingFrame" /> rather than duplicated between them.
    /// </summary>
    private void BlitToSwapchain(WebGpuDevice device, CommandEncoder* encoder, TextureView* swapView)
    {
        var api = device.Api;

        var gammaSlider = _game.Options.Gamma / 100.0f;
        _blitPipeline!.UploadUniforms(0.25f + gammaSlider * 1.5f);

        RenderPassColorAttachment colorAttach = new()
        {
            View = swapView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color(0, 0, 0, 1)
        };

        RenderPassDescriptor swapDesc = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttach
        };

        var swapPass = api.CommandEncoderBeginRenderPass(encoder, in swapDesc);

        // blit.wgsl reads a quad from a vertex buffer and declares the texture and sampler in
        // group 1, behind the uniform group every pipeline here carries at group 0.
        api.RenderPassEncoderSetPipeline(swapPass, _blitPipeline.Pipeline);
        _blitPipeline.BindUniformGroup(swapPass);
        api.RenderPassEncoderSetBindGroup(swapPass, 1,
            _offscreenFb!.GetBlitBindGroup(device, _blitPipeline.TextureBindGroupLayout), 0, null);
        _blitQuad!.Draw(swapPass);

        api.RenderPassEncoderEnd(swapPass);
        api.RenderPassEncoderRelease(swapPass);
    }

    /// <summary>
    ///     Blocks until the GPU finishes the copy <see cref="RenderFrame" /> just submitted, then
    ///     converts the mapped pixels into what <see cref="ScreenShotHelper" /> expects and saves
    ///     them.
    /// </summary>
    /// <remarks>
    ///     WebGPU has no synchronous readback — the browser-derived API is async-only — so blocking
    ///     means pumping <see cref="WebGpuDevice.Poll" /> in a loop until the map callback fires.
    ///     Not <c>Api.InstanceProcessEvents</c>: that call is an unimplemented stub in this
    ///     wgpu-native build and aborts the process outright the instant it runs, not throws — see
    ///     <see cref="WebGpuDevice.Poll" /> for why <c>wgpuDevicePoll</c> is the one that actually
    ///     works here. A capped loop, not an unbounded one, because a lost device would otherwise
    ///     spin this forever with nothing left to ever fire the callback.
    /// </remarks>
    private string ReadBackScreenshot(WebGpuDevice device, WgpuBuffer* buffer,
        uint width, uint height, uint bytesPerRow)
    {
        var api = device.Api;

        var mapped = false;
        var status = BufferMapAsyncStatus.Unknown;
        PfnBufferMapCallback callback = new((mapStatus, _) =>
        {
            status = mapStatus;
            mapped = true;
        });

        api.BufferMapAsync(buffer, MapMode.Read, 0, bytesPerRow * height, callback, null);

        for (var i = 0; !mapped && i < 10000; i++)
        {
            device.Poll();
        }

        if (!mapped || status != BufferMapAsyncStatus.Success)
        {
            return $"Failed to save: GPU readback did not complete ({status})";
        }

        var mappedPtr = api.BufferGetConstMappedRange(buffer, 0, bytesPerRow * height);
        ReadOnlySpan<byte> padded = new(mappedPtr, (int)(bytesPerRow * height));

        // _presentFb is always Bgra8Unorm or Rgba8Unorm (WebGpuDevice.ChooseSurfaceConfiguration
        // only ever picks one of those two) — everything else about the row is identical between
        // them, just the first and third byte swapped.
        var bgra = device.SurfaceFormat == TextureFormat.Bgra8Unorm;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            // WebGPU texture row 0 is the top of the image (blit.wgsl's quad maps texcoord v=0 to
            // clip-space +1), the opposite of GL's bottom-to-top rows that ScreenShotHelper expects
            // and flips back — so rows land reversed here to cancel that flip out correctly.
            var dstRow = (int)height - 1 - y;
            var srcRow = padded.Slice(y * (int)bytesPerRow, (int)width * 4);
            for (var x = 0; x < width; x++)
            {
                var c0 = srcRow[x * 4];
                var c1 = srcRow[x * 4 + 1];
                var c2 = srcRow[x * 4 + 2];
                var dst = (dstRow * (int)width + x) * 3;
                if (bgra)
                {
                    rgb[dst] = c2;
                    rgb[dst + 1] = c1;
                    rgb[dst + 2] = c0;
                }
                else
                {
                    rgb[dst] = c0;
                    rgb[dst + 1] = c1;
                    rgb[dst + 2] = c2;
                }
            }
        }

        api.BufferUnmap(buffer);
        return ScreenShotHelper.saveScreenshot(_game.GameDataDir, (int)width, (int)height, rgb);
    }

    /// <summary>
    ///     Draws the HUD and the current screen over the world, into the offscreen framebuffer.
    /// </summary>
    /// <remarks>
    ///     A pass of its own rather than more draws in the world's, because the interface is not all
    ///     flat: an item icon and the inventory's mob preview are 3D geometry that needs a depth
    ///     buffer, and the pipelines the draw target builds all declare one. It reuses the offscreen
    ///     framebuffer's own depth texture, which the world pass has finished writing to by the time
    ///     this begins, and clears it — nothing here is meant to be occluded by the world.
    /// </remarks>
    private void RenderInterfacePass(WebGpuDevice device, CommandEncoder* encoder, float tickDelta)
    {
        var api = device.Api;
        var pass = BeginSwapPass(api, encoder, _offscreenFb!.ColorView, _offscreenFb.DepthView);

        // The inventory's mob preview and the held item render through the entity dispatcher, which
        // reads the camera, the world and the font from here. WorldRenderer's frame is what normally
        // fills them in, and this renderer does not drive that frame.
        if (_game.World is not null && _game.Player is not null)
        {
            EntityRenderDispatcher.Instance.CacheRenderInfo(
                _game.World, _game.TextureManager, _game.TextRenderer, _game.Camera, _game.Options, tickDelta);
        }

        _drawTarget.BeginPass(pass, _offscreenFb.Width, _offscreenFb.Height);

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
    ///     Draws the first-person hand in its own pass over the offscreen framebuffer, with the
    ///     colour it already holds kept and its depth reset to far.
    /// </summary>
    /// <remarks>
    ///     WebGPU has no call to clear a buffer partway through a pass — a GL depth clear ahead of
    ///     the hand draw becomes, here, ending the world pass and opening a second one on the same
    ///     attachments instead. Without this the hand is depth-tested against the world just drawn
    ///     and gets clipped by geometry near the camera.
    /// </remarks>
    private void RenderFirstPersonHand(WgpuFramebuffer offscreenFb, CommandEncoder* encoder, float tickDelta)
    {
        var api = WebGpuDevice.Current!.Api;

        var handPass = offscreenFb.BeginPass(encoder, default,
            false);
        _drawTarget.BeginPass(handPass, offscreenFb.Width, offscreenFb.Height);

        try
        {
            _game.GameRenderer.RenderFirstPersonHandIfNeeded(tickDelta);
        }
        finally
        {
            _drawTarget.EndPass();
        }

        api.RenderPassEncoderEnd(handPass);
        api.RenderPassEncoderRelease(handPass);
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
            DepthSlice = unchecked((uint)-1)
        };

        RenderPassDepthStencilAttachment depthAttach = new()
        {
            View = depthView,
            DepthLoadOp = LoadOp.Clear,
            DepthStoreOp = StoreOp.Store,
            DepthClearValue = 1.0f,
            StencilLoadOp = LoadOp.Undefined,
            StencilStoreOp = StoreOp.Undefined
        };

        RenderPassDescriptor descriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttach,
            DepthStencilAttachment = depthView is null ? null : &depthAttach
        };

        return api.CommandEncoderBeginRenderPass(encoder, in descriptor);
    }

    private void EnsureResources(WebGpuDevice device)
    {
        if (_offscreenFb == null)
        {
            // CopySrc on the depth texture: WgpuCloudBlurPass copies it into its own capture buffer
            // so clouds draw depth-tested against terrain already in this frame.
            _offscreenFb = WgpuFramebuffer.CreateColorDepth(device,
                device.Width, device.Height, device.SurfaceFormat, TextureUsage.CopySrc);
        }

        if (_blitPipeline == null)
        {
            var blitWgsl = AssetManager.Instance.GetAsset("shaders/blit.wgsl").GetTextContent();

            BindGroupLayoutEntry[] blitUniformEntries =
            [
                new()
                {
                    Binding = 0,
                    Visibility = ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout
                    {
                        Type = BufferBindingType.Uniform,
                        MinBindingSize = 64
                    }
                }
            ];

            BindGroupLayoutEntry[] blitTextureEntries =
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

            var blitAttrs = stackalloc VertexAttribute[2];
            blitAttrs[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 0,
                ShaderLocation = 0
            };
            blitAttrs[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x2,
                Offset = 12,
                ShaderLocation = 1
            };

            VertexBufferLayout blitLayout = new()
            {
                ArrayStride = BlitQuadStride,
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 2,
                Attributes = blitAttrs
            };

            _blitPipeline = new WgpuPipeline(
                device, blitWgsl, "vs_main",
                64,
                blitUniformEntries,
                blitTextureEntries,
                &blitLayout, 1,
                RenderState.PostProcess,
                device.SurfaceFormat);

            _blitQuad = CreateBlitQuad(device);
        }

        // The texture ImGui's Game Viewport samples while the F3 debug window is open — see
        // RenderFrame's gamma pass. Colour-only: it is a copy destination for a full-screen quad,
        // never a render target anything depth-tests against.
        if (_presentFb == null)
        {
            _presentFb = WgpuFramebuffer.CreateColor(device,
                device.Width, device.Height, device.SurfaceFormat);
        }

        if (_cloudBlurPass == null)
        {
            _cloudBlurPass = new WgpuCloudBlurPass(device, _drawTarget, _offscreenFb, _blitQuad!);
            RenderSystem.CloudBlurPassOrNull = _cloudBlurPass;
        }

        if (_imguiWgpu == null)
        {
            _imguiWgpu = new ImGuiWgpuBackend(device);
        }
    }

    /// <summary>
    ///     The screen-covering quad the offscreen colour texture is sampled onto, in clip space.
    /// </summary>
    private static WgpuMesh CreateBlitQuad(WebGpuDevice device)
    {
        float[] vertices =
        [
            -1, -1, 0, 0, 1,
            1, -1, 0, 1, 1,
            1, 1, 0, 1, 0,
            1, 1, 0, 1, 0,
            -1, 1, 0, 0, 0,
            -1, -1, 0, 0, 1
        ];

        return new WgpuMesh(device, MemoryMarshal.AsBytes(vertices.AsSpan()), BlitQuadStride);
    }
}
