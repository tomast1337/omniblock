using System.Numerics;
using System.Runtime.InteropServices;
using BetaSharp.Client.Diagnostics;
using BetaSharp.Client.Rendering.Core;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

public static unsafe class WebGpuPreview
{
    private static readonly Silk.NET.WebGPU.Color s_clearColor = new(0.12, 0.14, 0.18, 1.0);

    /// <summary>The vertex <c>gbuffers_basic.wgsl</c> reads at locations 0, 1 and 3.</summary>
    /// <remarks>
    ///     The real Tessellator packs colour as 4 unsigned bytes (Unorm8x4) and the normal as
    ///     3 signed bytes (Snorm8x3) at offsets 20 and 24 of a 36-byte struct. Snorm8x3 has no
    ///     WebGPU vertex format, so the Tessellator path will need to widen it. This demo uses
    ///     float components throughout; the vertex count matters more than the exact byte layout
    ///     here.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct DemoVertex(Vector3 pos, Vector4 color, Vector3 normal)
    {
        public Vector3 Position = pos;
        public Vector4 Color = color;
        public Vector3 Normal = normal;

        public static readonly uint Stride = (uint)sizeof(DemoVertex);
    }

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
        using WgpuPipeline pipeline = CreateDemoPipeline(device);
        using WgpuMesh cube = CreateCube(device);

        long startTick = Environment.TickCount64;

        try
        {
            while (!Display.isCloseRequested())
            {
                Display.processMessages();
                Resize(device);
                DrawFrame(device, backend, pipeline, cube, startTick);
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

    private static WgpuPipeline CreateDemoPipeline(WebGpuDevice device)
    {
        string source = AssetManager.Instance.getAsset("shaders/gbuffers_basic.wgsl").GetTextContent();

        Silk.NET.WebGPU.WebGPU api = device.Api;

        // Shader module.
        byte* code = (byte*)SilkMarshal.StringToPtr(source);
        ShaderModule* module;
        try
        {
            ShaderModuleWGSLDescriptor wgslDesc = new()
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };
            ShaderModuleDescriptor shaderDesc = default;
            shaderDesc.NextInChain = (ChainedStruct*)&wgslDesc;
            module = api.DeviceCreateShaderModule(device.Device, in shaderDesc);
        }
        finally { SilkMarshal.Free((nint)code); }

        // Bind group layout — one uniform buffer at binding 0.
        BindGroupLayoutEntry uniformEntry = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 128 },
        };
        BindGroupLayoutDescriptor bglDesc = new() { EntryCount = 1, Entries = &uniformEntry };
        BindGroupLayout* bindGroupLayout = api.DeviceCreateBindGroupLayout(device.Device, in bglDesc);

        // Pipeline layout.
        BindGroupLayout** ppLayout = &bindGroupLayout;
        PipelineLayoutDescriptor plDesc = new() { BindGroupLayoutCount = 1, BindGroupLayouts = ppLayout };
        PipelineLayout* pipelineLayout = api.DeviceCreatePipelineLayout(device.Device, in plDesc);

        // Vertex layout — locations 0, 1 and 3 matching gbuffers_basic's VertexInput.
        VertexAttribute* attrs = stackalloc VertexAttribute[3];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 12, ShaderLocation = 1 };
        attrs[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 28, ShaderLocation = 3 };
        VertexBufferLayout vbLayout = new()
        {
            ArrayStride = DemoVertex.Stride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 3,
            Attributes = attrs,
        };

        BlendComponent idBlend = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.Zero };
        BlendState blend = new() { Color = idBlend, Alpha = idBlend };
        ColorTargetState colorTarget = new()
        {
            Format = device.SurfaceFormat,
            Blend = &blend,
            WriteMask = ColorWriteMask.All,
        };

        byte* vsEntry = (byte*)SilkMarshal.StringToPtr("vs_main");
        byte* fsEntry = (byte*)SilkMarshal.StringToPtr("fs_main");
        RenderPipeline* pipeline;
        try
        {
            FragmentState fragment = new() { Module = module, EntryPoint = fsEntry, TargetCount = 1, Targets = &colorTarget };
            RenderPipelineDescriptor rpDesc = new()
            {
                Layout = pipelineLayout,
                Vertex = new VertexState { Module = module, EntryPoint = vsEntry, BufferCount = 1, Buffers = &vbLayout },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = Silk.NET.WebGPU.CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };
            pipeline = api.DeviceCreateRenderPipeline(device.Device, in rpDesc);
        }
        finally { SilkMarshal.Free((nint)vsEntry); SilkMarshal.Free((nint)fsEntry); }

        // Uniform buffer + bind group.
        nuint uniformSize = 128;
        BufferDescriptor bufDesc = new() { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)uniformSize };
        WgpuBuffer* uniformBuffer = api.DeviceCreateBuffer(device.Device, in bufDesc);

        BindGroupEntry bgEntry = new() { Binding = 0, Buffer = uniformBuffer, Offset = 0, Size = (ulong)uniformSize };
        BindGroupDescriptor bgDesc = new() { Layout = bindGroupLayout, EntryCount = 1, Entries = &bgEntry };
        BindGroup* uniformBindGroup = api.DeviceCreateBindGroup(device.Device, in bgDesc);

        return new WgpuPipeline(module, bindGroupLayout, pipelineLayout, pipeline, uniformBuffer, uniformBindGroup, device);
    }

    /// <summary>A unit cube centered at the origin, one colour per face.</summary>
    private static WgpuMesh CreateCube(WebGpuDevice device)
    {
        Vector4[] faceColors =
        [
            new(1, 0, 0, 1), // +X red
            new(0, 1, 1, 1), // -X cyan
            new(0, 1, 0, 1), // +Y green
            new(1, 0, 1, 1), // -Y magenta
            new(0, 0, 1, 1), // +Z blue
            new(1, 1, 0, 1), // -Z yellow
        ];

        // Each face: 6 vertices (2 triangles). Normals point outward per face.
        DemoVertex[] vertices = new DemoVertex[36];
        ushort[] indices = new ushort[36];

        // Face order: +X, -X, +Y, -Y, +Z, -Z, each with a distinctive colour.
        (Vector3 Origin, Vector3 U, Vector3 V, Vector3 N)[] faces =
        [
            (new(0.5f, -0.5f, -0.5f), new(0, 0, 1), new(0, 1, 0), Vector3.UnitX),
            (new(-0.5f, -0.5f, 0.5f), new(0, 0, -1), new(0, 1, 0), -Vector3.UnitX),
            (new(-0.5f, 0.5f, -0.5f), new(1, 0, 0), new(0, 0, 1), Vector3.UnitY),
            (new(-0.5f, -0.5f, 0.5f), new(1, 0, 0), new(0, 0, -1), -Vector3.UnitY),
            (new(-0.5f, -0.5f, 0.5f), new(1, 0, 0), new(0, 1, 0), Vector3.UnitZ),
            (new(0.5f, -0.5f, -0.5f), new(-1, 0, 0), new(0, 1, 0), -Vector3.UnitZ),
        ];

        for (int f = 0; f < 6; f++)
        {
            var (origin, u, v, n) = faces[f];
            Vector4 color = faceColors[f];
            int vi = f * 6;

            // Two triangles: 0-1-2 and 2-3-0. CCW winding for the front face.
            vertices[vi + 0] = new(origin, color, n);
            vertices[vi + 1] = new(origin + u, color, n);
            vertices[vi + 2] = new(origin + u + v, color, n);
            vertices[vi + 3] = new(origin + u + v, color, n);
            vertices[vi + 4] = new(origin + v, color, n);
            vertices[vi + 5] = new(origin, color, n);

            // Indexed from the same offset.
            ushort baseIndex = (ushort)vi;
            for (int k = 0; k < 6; k++)
            {
                indices[vi + k] = (ushort)(vi + k);
            }
        }

        ReadOnlySpan<byte> vertexData = MemoryMarshal.AsBytes(vertices.AsSpan());
        ReadOnlySpan<byte> indexData = MemoryMarshal.AsBytes(indices.AsSpan());

        return new WgpuMesh(device, vertexData, DemoVertex.Stride, indexData, IndexFormat.Uint16);
    }

    private static void DrawFrame(
        WebGpuDevice device,
        ImGuiWgpuBackend backend,
        WgpuPipeline pipeline,
        WgpuMesh cube,
        long startTick)
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
            DepthSlice = unchecked((uint)-1),
        };

        RenderPassDescriptor passDescriptor = new()
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &attachment,
        };

        RenderPassEncoder* pass = api.CommandEncoderBeginRenderPass(encoder, in passDescriptor);

        // --- cube ------------------------------------------------------------
        UploadDemoUniforms(pipeline, startTick, device.Width, device.Height);
        api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
        api.RenderPassEncoderSetBindGroup(pass, 0, pipeline.UniformBindGroup, 0, null);
        cube.Draw(pass);
        // ---------------------------------------------------------------------

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

    private static void UploadDemoUniforms(WgpuPipeline pipeline, long startTick, uint width, uint height)
    {
        float elapsed = (Environment.TickCount64 - startTick) / 1000.0f;
        float aspect = (float)width / Math.Max(1u, height);

        // Two-axis rotation.
        float angleY = elapsed * 0.7f;
        float angleX = elapsed * 0.5f;
        Matrix4x4 modelView =
            Matrix4x4.CreateRotationY(angleY) *
            Matrix4x4.CreateRotationX(angleX) *
            Matrix4x4.CreateTranslation(0.0f, 0.0f, -2.5f);

        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f, aspect, 0.1f, 100.0f);

        // Convert GL-style [-1,1] Z to WebGPU [0,1].
        projection.M33 = projection.M33 * 0.5f + 0.5f * projection.M43;
        projection.M34 = projection.M34 * 0.5f;

        // Two mat4x4s, 128 bytes: modelView then projection, matching Uniforms layout.
        Span<float> data =
        [
            modelView.M11, modelView.M12, modelView.M13, modelView.M14,
            modelView.M21, modelView.M22, modelView.M23, modelView.M24,
            modelView.M31, modelView.M32, modelView.M33, modelView.M34,
            modelView.M41, modelView.M42, modelView.M43, modelView.M44,
            projection.M11, projection.M12, projection.M13, projection.M14,
            projection.M21, projection.M22, projection.M23, projection.M24,
            projection.M31, projection.M32, projection.M33, projection.M34,
            projection.M41, projection.M42, projection.M43, projection.M44,
        ];

        fixed (float* p = data)
        {
            pipeline.UploadUniforms(p, 128);
        }
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
                "Phase 3: a spinning cube, 36 vertices + 36 indices drawn through "
                + "gbuffers_basic.wgsl. If the cube spins below the ImGui layer, then "
                + "vertex buffer upload, index buffer upload and indexed draw all work.");
        }

        ImGui.End();
    }
}
