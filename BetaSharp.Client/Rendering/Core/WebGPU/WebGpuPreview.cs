using System.Numerics;
using BetaSharp.Client.Diagnostics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     The WebGPU backend's own window and loop: a cleared surface with the ImGui overlay drawn
///     over it, and nothing of the game.
/// </summary>
/// <remarks>
///     <para>
///         Reached with <c>--webgpu</c>. The game's loop reaches OpenGL through a hundred call
///         sites that have no WebGPU answer yet, so hosting the new backend inside it would mean
///         maintaining a client that cannot draw a world for as long as the port takes. This
///         instead exercises what the backend can already do — device, surface, swap chain, shader
///         compilation, pipeline creation, buffer and texture upload, scissor, indexed drawing and
///         present — against a data source that is known-good, and grows to host the real
///         renderers as they are ported.
///     </para>
///     <para>
///         It shares <see cref="Display" /> with the OpenGL client rather than opening its own
///         window, so the window, input and fullscreen handling stay in one place and the
///         difference between the backends stays the one line that asks for no client API.
///     </para>
/// </remarks>
public static unsafe class WebGpuPreview
{
    /// <summary>What an empty surface clears to, so "nothing drew" is distinguishable from "nothing ran".</summary>
    private static readonly Silk.NET.WebGPU.Color s_clearColor = new(0.12, 0.14, 0.18, 1.0);

    public static void Run()
    {
        Display.Backend = GraphicsBackend.WebGpu;
        Display.setTitle("OmniBlock — WebGPU preview");
        Display.setDisplayMode(new DisplayMode(1280, 720));
        Display.create();

        using WebGpuDevice device = WebGpuDevice.Create(
            Display.getWindow(), Display.getFramebufferWidth(), Display.getFramebufferHeight());

        ImGui.CreateContext();
        ImGuiImplGLFW.SetCurrentContext(ImGui.GetCurrentContext());

        ImGuiIO* io = ImGui.GetIO();
        io->ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;

        ImGuiImplGLFW.InitForOther((GLFWwindow*)Display.GetWindowHandle(), true);

        using ImGuiWgpuBackend backend = new(device);

        try
        {
            while (!Display.isCloseRequested())
            {
                Display.processMessages();
                Resize(device);
                DrawFrame(device, backend);
            }
        }
        finally
        {
            ImGuiImplGLFW.Shutdown();
            ImGui.DestroyContext();
            Display.destroy();
        }
    }

    private static void Resize(WebGpuDevice device)
    {
        uint width = (uint)Math.Max(1, Display.getFramebufferWidth());
        uint height = (uint)Math.Max(1, Display.getFramebufferHeight());

        if (width != device.Width || height != device.Height)
        {
            device.Configure(width, height);
        }
    }

    private static void DrawFrame(WebGpuDevice device, ImGuiWgpuBackend backend)
    {
        ImGuiImplGLFW.NewFrame();

        ImGuiIO* io = ImGui.GetIO();
        int windowWidth = Math.Max(1, Display.getWidth());
        int windowHeight = Math.Max(1, Display.getHeight());
        io->DisplaySize = new Vector2(windowWidth, windowHeight);
        io->DisplayFramebufferScale = new Vector2(
            Display.getFramebufferWidth() / (float)windowWidth,
            Display.getFramebufferHeight() / (float)windowHeight);

        ImGui.NewFrame();
        DrawStatusWindow(device);
        ImGui.Render();

        TextureView* target = device.AcquireFrame();
        if (target is null)
        {
            // The surface was reconfigured; ImGui's draw data is dropped with the frame.
            return;
        }

        Silk.NET.WebGPU.WebGPU api = device.Api;

        CommandEncoderDescriptor encoderDescriptor = default;
        CommandEncoder* encoder = api.DeviceCreateCommandEncoder(device.Device, in encoderDescriptor);

        RenderPassColorAttachment attachment = new()
        {
            View = target,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = s_clearColor,

            // Not an array-layer view, but wgpu still validates the field and rejects zero.
            DepthSlice = unchecked((uint)-1),
        };

        RenderPassDescriptor passDescriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &attachment,
        };

        RenderPassEncoder* pass = api.CommandEncoderBeginRenderPass(encoder, in passDescriptor);
        backend.RenderDrawData(ImGui.GetDrawData(), pass);
        api.RenderPassEncoderEnd(pass);
        api.RenderPassEncoderRelease(pass);

        CommandBufferDescriptor bufferDescriptor = default;
        CommandBuffer* commands = api.CommandEncoderFinish(encoder, in bufferDescriptor);
        api.QueueSubmit(device.Queue, 1, &commands);

        api.CommandBufferRelease(commands);
        api.CommandEncoderRelease(encoder);
        api.TextureViewRelease(target);

        device.Present();
    }

    private static void DrawStatusWindow(WebGpuDevice device)
    {
        ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("WebGPU"))
        {
            ImGuiTextSafe.Text($"Surface format: {device.SurfaceFormat}");
            ImGuiTextSafe.Text($"Surface size: {device.Width} x {device.Height}");
            ImGuiTextSafe.Text($"Frame time: {1000.0f / ImGui.GetIO().Framerate:F2} ms");

            ImGui.Separator();
            ImGuiTextSafe.TextWrapped(
                "Everything above this window is a cleared surface. If this text is legible, is "
                + "clipped to the window, and the window drags and resizes, then the device, swap "
                + "chain, shader, pipeline, buffer and texture uploads, scissor and indexed draw "
                + "all work.");
        }

        ImGui.End();
    }
}
