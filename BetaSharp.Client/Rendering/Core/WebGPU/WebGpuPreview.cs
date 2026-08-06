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

    [StructLayout(LayoutKind.Sequential, Pack = 4, Size = 36)]
    private struct DemoVertex(
        float x, float y, float z,
        float u, float v,
        uint color,
        int normalPacked,
        int arrayLayer,
        uint light)
    {
        public float X = x, Y = y, Z = z;
        public float U = u, V = v;
        public uint Color = color;
        public int NormalPacked = normalPacked;
        public int ArrayLayer = arrayLayer;
        public uint Light = light;

        public static readonly uint Stride = 36;
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
        using WgpuPipeline pipeline = CreateTexturedPipeline(device, WgpuFramebuffer.DepthFormat);
        using WgpuTexture checkerboard = CreateCheckerboard(device, pipeline.TextureBindGroupLayout);
        using WgpuMesh cube = CreateTexturedCube(device);
        using WgpuFramebuffer fb = WgpuFramebuffer.CreateColorDepth(device,
            (uint)Display.getFramebufferWidth(), (uint)Display.getFramebufferHeight(),
            device.SurfaceFormat);
        CreateBlit(device, fb, out WgpuPipeline blitPipeline, out WgpuMesh blitQuad, out BindGroup* blitBindGroup);

        long startTick = Environment.TickCount64;

        try
        {
            while (!Display.isCloseRequested())
            {
                Display.processMessages();
                Resize(device);
                DrawFrame(device, backend, pipeline, checkerboard, cube, fb, blitPipeline, blitQuad, blitBindGroup, startTick);
            }
        }
        finally
        {
            if (blitBindGroup is not null) device.Api.BindGroupRelease(blitBindGroup);
            ImGuiImplGLFW.Shutdown();
            ImGui.DestroyContext();
            Display.destroy();
        }
    }

    private static void Resize(WebGpuDevice device)
    {
        uint w = (uint)Math.Max(1, Display.getFramebufferWidth());
        uint h = (uint)Math.Max(1, Display.getFramebufferHeight());
        if (w != device.Width || h != device.Height)
        {
            device.Configure(w, h);
        }
    }

    private static WgpuTexture CreateCheckerboard(WebGpuDevice device, BindGroupLayout* texBgl)
    {
        const int size = 16;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool white = ((x ^ y) & 1) == 0;
                int i = (y * size + x) * 4;
                pixels[i + 0] = white ? (byte)255 : (byte)0;
                pixels[i + 1] = white ? (byte)0 : (byte)255;
                pixels[i + 2] = white ? (byte)255 : (byte)0;
                pixels[i + 3] = 255;
            }

        return new WgpuTexture(device, size, size, pixels, texBgl);
    }

    private static WgpuPipeline CreateTexturedPipeline(WebGpuDevice device,
        TextureFormat depthFormat = TextureFormat.Undefined)
    {
        string source = AssetManager.Instance.getAsset("shaders/gbuffers_textured.wgsl").GetTextContent();
        Silk.NET.WebGPU.WebGPU api = device.Api;

        byte* code = (byte*)SilkMarshal.StringToPtr(source);
        ShaderModule* module;
        try
        {
            ShaderModuleWGSLDescriptor wgslDesc = new()
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };
            ShaderModuleDescriptor sd = default;
            sd.NextInChain = (ChainedStruct*)&wgslDesc;
            module = api.DeviceCreateShaderModule(device.Device, in sd);
        }
        finally { SilkMarshal.Free((nint)code); }

        // Group 0: uniforms.
        BindGroupLayoutEntry ue = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 128 },
        };
        BindGroupLayoutDescriptor ubgl = new() { EntryCount = 1, Entries = &ue };
        BindGroupLayout* uniformBgl = api.DeviceCreateBindGroupLayout(device.Device, in ubgl);

        // Group 1: 2D texture + sampler.
        BindGroupLayoutEntry* te = stackalloc BindGroupLayoutEntry[2];
        te[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D },
        };
        te[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        BindGroupLayoutDescriptor tbgl = new() { EntryCount = 2, Entries = te };
        BindGroupLayout* texBgl = api.DeviceCreateBindGroupLayout(device.Device, in tbgl);

        BindGroupLayout** pp = stackalloc BindGroupLayout*[2];
        pp[0] = uniformBgl; pp[1] = texBgl;
        PipelineLayoutDescriptor pl = new() { BindGroupLayoutCount = 2, BindGroupLayouts = pp };
        PipelineLayout* layout = api.DeviceCreatePipelineLayout(device.Device, in pl);

        // 3 vertex attributes: position, color, texcoord.
        VertexAttribute* attrs = stackalloc VertexAttribute[3];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attrs[1] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 20, ShaderLocation = 1 };
        attrs[2] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 12, ShaderLocation = 2 };
        VertexBufferLayout vb = new()
        {
            ArrayStride = DemoVertex.Stride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 3,
            Attributes = attrs,
        };

        BlendComponent id = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.Zero };
        BlendState blend = new() { Color = id, Alpha = id };
        ColorTargetState ct = new() { Format = device.SurfaceFormat, Blend = &blend, WriteMask = ColorWriteMask.All };

        byte* vs = (byte*)SilkMarshal.StringToPtr("vs_main");
        byte* fs = (byte*)SilkMarshal.StringToPtr("fs_main");
        RenderPipeline* rp;
        try
        {
            DepthStencilState ds = default;
            DepthStencilState* pDs = null;
            if (depthFormat != TextureFormat.Undefined)
            {
                ds = new DepthStencilState
                {
                    Format = depthFormat,
                    DepthWriteEnabled = true,
                    DepthCompare = CompareFunction.LessEqual,
                    StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
                    StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
                };
                pDs = &ds;
            }

            FragmentState fg = new() { Module = module, EntryPoint = fs, TargetCount = 1, Targets = &ct };
            RenderPipelineDescriptor rpd = new()
            {
                Layout = layout,
                Vertex = new VertexState { Module = module, EntryPoint = vs, BufferCount = 1, Buffers = &vb },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = Silk.NET.WebGPU.CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                DepthStencil = pDs,
                Fragment = &fg,
            };
            rp = api.DeviceCreateRenderPipeline(device.Device, in rpd);
        }
        finally { SilkMarshal.Free((nint)vs); SilkMarshal.Free((nint)fs); }

        nuint us = 128;
        BufferDescriptor bd = new() { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)us };
        WgpuBuffer* ubuf = api.DeviceCreateBuffer(device.Device, in bd);
        BindGroupEntry bge = new() { Binding = 0, Buffer = ubuf, Offset = 0, Size = (ulong)us };
        BindGroupDescriptor bgd = new() { Layout = uniformBgl, EntryCount = 1, Entries = &bge };
        BindGroup* ubg = api.DeviceCreateBindGroup(device.Device, in bgd);

        return new WgpuPipeline(module, uniformBgl, texBgl, layout, rp, ubuf, ubg, device);
    }

    private static WgpuMesh CreateTexturedCube(WebGpuDevice device)
    {
        DemoVertex[] v = new DemoVertex[36];
        (Vector3 O, Vector3 U, Vector3 V)[] faces =
        [
            (new(0.5f, -0.5f, -0.5f), new(0, 0, 1), new(0, 1, 0)),
            (new(-0.5f, -0.5f, 0.5f), new(0, 0, -1), new(0, 1, 0)),
            (new(-0.5f, 0.5f, -0.5f), new(1, 0, 0), new(0, 0, 1)),
            (new(-0.5f, -0.5f, 0.5f), new(1, 0, 0), new(0, 0, -1)),
            (new(-0.5f, -0.5f, 0.5f), new(1, 0, 0), new(0, 1, 0)),
            (new(0.5f, -0.5f, -0.5f), new(-1, 0, 0), new(0, 1, 0)),
        ];
        const uint white = 0xFFFFFFFF;
        const int noArray = -1;
        const uint fullLight = 60u | (60u << 8);

        for (int f = 0; f < 6; f++)
        {
            var (o, u, uv) = faces[f];
            int vi = f * 6;
            v[vi + 0] = Vtx(o, 0, 0);
            v[vi + 1] = Vtx(o + u, 1, 0);
            v[vi + 2] = Vtx(o + u + uv, 1, 1);
            v[vi + 3] = Vtx(o + u + uv, 1, 1);
            v[vi + 4] = Vtx(o + uv, 0, 1);
            v[vi + 5] = Vtx(o, 0, 0);
        }

        return new WgpuMesh(device, MemoryMarshal.AsBytes(v.AsSpan()), DemoVertex.Stride);
    }

    private static DemoVertex Vtx(Vector3 pos, float u, float v) =>
        new(pos.X, pos.Y, pos.Z, u, v, 0xFFFFFFFF, 0x7F7F7F00, -1, 60u | (60u << 8));

    [StructLayout(LayoutKind.Sequential)]
    private struct BlitVertex(Vector3 pos, Vector2 uv)
    {
        public Vector3 Position = pos;
        public Vector2 Texcoord = uv;
        public static readonly uint Stride = (uint)sizeof(BlitVertex);
    }

    private static void CreateBlit(
        WebGpuDevice device, WgpuFramebuffer fb,
        out WgpuPipeline pipeline, out WgpuMesh quad, out BindGroup* bindGroup)
    {
        string source = AssetManager.Instance.getAsset("shaders/blit.wgsl").GetTextContent();
        Silk.NET.WebGPU.WebGPU api = device.Api;

        byte* code = (byte*)SilkMarshal.StringToPtr(source);
        ShaderModule* module;
        try
        {
            ShaderModuleWGSLDescriptor wgslDesc = new()
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };
            ShaderModuleDescriptor sd = default;
            sd.NextInChain = (ChainedStruct*)&wgslDesc;
            module = api.DeviceCreateShaderModule(device.Device, in sd);
        }
        finally { SilkMarshal.Free((nint)code); }

        // Uniform bind group layout (required but unused — the blit shader declares it).
        BindGroupLayoutEntry ue = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 64 },
        };
        BindGroupLayoutDescriptor ubgl = new() { EntryCount = 1, Entries = &ue };
        BindGroupLayout* uniformBgl = api.DeviceCreateBindGroupLayout(device.Device, in ubgl);

        // Texture bind group layout.
        BindGroupLayoutEntry* te = stackalloc BindGroupLayoutEntry[2];
        te[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D },
        };
        te[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        BindGroupLayoutDescriptor tbgl = new() { EntryCount = 2, Entries = te };
        BindGroupLayout* texBgl = api.DeviceCreateBindGroupLayout(device.Device, in tbgl);

        BindGroupLayout** pp = stackalloc BindGroupLayout*[2]; pp[0] = uniformBgl; pp[1] = texBgl;
        PipelineLayoutDescriptor pl = new() { BindGroupLayoutCount = 2, BindGroupLayouts = pp };
        PipelineLayout* layout = api.DeviceCreatePipelineLayout(device.Device, in pl);

        VertexAttribute* attrs = stackalloc VertexAttribute[2];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 12, ShaderLocation = 1 };
        VertexBufferLayout vb = new() { ArrayStride = BlitVertex.Stride, StepMode = VertexStepMode.Vertex, AttributeCount = 2, Attributes = attrs };

        BlendComponent id = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.Zero };
        BlendState blend = new() { Color = id, Alpha = id };
        ColorTargetState ct = new() { Format = device.SurfaceFormat, Blend = &blend, WriteMask = ColorWriteMask.All };

        byte* vs = (byte*)SilkMarshal.StringToPtr("vs_main");
        byte* fs = (byte*)SilkMarshal.StringToPtr("fs_main");
        RenderPipeline* rp;
        try
        {
            FragmentState fg = new() { Module = module, EntryPoint = fs, TargetCount = 1, Targets = &ct };
            rp = api.DeviceCreateRenderPipeline(device.Device, new RenderPipelineDescriptor
            {
                Layout = layout,
                Vertex = new VertexState { Module = module, EntryPoint = vs, BufferCount = 1, Buffers = &vb },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = Silk.NET.WebGPU.CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fg,
            });
        }
        finally { SilkMarshal.Free((nint)vs); SilkMarshal.Free((nint)fs); }

        // Dummy uniform buffer (required by layout even though unused).
        BufferDescriptor bd = new() { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = 64 };
        WgpuBuffer* ubuf = api.DeviceCreateBuffer(device.Device, in bd);
        BindGroupEntry bge = new() { Binding = 0, Buffer = ubuf, Offset = 0, Size = 64 };
        BindGroupDescriptor bgd = new() { Layout = uniformBgl, EntryCount = 1, Entries = &bge };
        BindGroup* ubg = api.DeviceCreateBindGroup(device.Device, in bgd);

        // Bind group: framebuffer colour + linear sampler.
        SamplerDescriptor sDesc = new()
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        };
        Sampler* sampler = api.DeviceCreateSampler(device.Device, in sDesc);

        BindGroupEntry* bgEntries = stackalloc BindGroupEntry[2];
        bgEntries[0] = new BindGroupEntry { Binding = 0, TextureView = fb.ColorView };
        bgEntries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler };
        BindGroupDescriptor bgDesc = new() { Layout = texBgl, EntryCount = 2, Entries = bgEntries };
        BindGroup* bg = api.DeviceCreateBindGroup(device.Device, in bgDesc);

        // Full-screen quad in NDC.
        BlitVertex[] quadVerts =
        [
            new(new(-1, -1, 0), new(0, 0)), new(new(1, -1, 0), new(1, 0)), new(new(1, 1, 0), new(1, 1)),
            new(new(1, 1, 0), new(1, 1)), new(new(-1, 1, 0), new(0, 1)), new(new(-1, -1, 0), new(0, 0)),
        ];

        pipeline = new WgpuPipeline(module, uniformBgl, texBgl, layout, rp, ubuf, ubg, device);
        quad = new WgpuMesh(device, MemoryMarshal.AsBytes(quadVerts.AsSpan()), BlitVertex.Stride);
        bindGroup = bg;
    }

    private static void DrawFrame(
        WebGpuDevice device, ImGuiWgpuBackend backend,
        WgpuPipeline pipeline, WgpuTexture tex, WgpuMesh cube, WgpuFramebuffer fb,
        WgpuPipeline blitPipeline, WgpuMesh blitQuad, BindGroup* blitBindGroup, long startTick)
    {
        ImGuiImplGLFW.NewFrame();
        ImGuiIO* io = ImGui.GetIO();
        int ww = Math.Max(1, Display.getWidth()), wh = Math.Max(1, Display.getHeight());
        io->DisplaySize = new Vector2(ww, wh);
        io->DisplayFramebufferScale = new Vector2(Display.getFramebufferWidth() / (float)ww, Display.getFramebufferHeight() / (float)wh);

        ImGui.NewFrame();
        DrawStatusWindow(device);
        ImGui.Render();

        TextureView* target = device.AcquireFrame();
        if (target is null) return;

        Silk.NET.WebGPU.WebGPU api = device.Api;
        CommandEncoder* enc = api.DeviceCreateCommandEncoder(device.Device, default(CommandEncoderDescriptor));

        // Pass 1: cube into the framebuffer with depth.
        RenderPassEncoder* offPass = fb.BeginPass(enc, s_clearColor);
        UploadDemoUniforms(pipeline, startTick, device.Width, device.Height);
        api.RenderPassEncoderSetPipeline(offPass, pipeline.Pipeline);
        api.RenderPassEncoderSetBindGroup(offPass, 0, pipeline.UniformBindGroup, 0, null);
        api.RenderPassEncoderSetBindGroup(offPass, 1, tex.BindGroup, 0, null);
        cube.Draw(offPass);
        api.RenderPassEncoderEnd(offPass);
        api.RenderPassEncoderRelease(offPass);

        // Pass 2: blit framebuffer colour to swapchain, then ImGui on top.
        RenderPassColorAttachment att = new()
        {
            View = target,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = s_clearColor,
            DepthSlice = unchecked((uint)-1),
        };
        RenderPassDescriptor pd = new() { ColorAttachmentCount = 1, ColorAttachments = &att };
        RenderPassEncoder* pass = api.CommandEncoderBeginRenderPass(enc, in pd);

        api.RenderPassEncoderSetPipeline(pass, blitPipeline.Pipeline);
        api.RenderPassEncoderSetBindGroup(pass, 0, blitPipeline.UniformBindGroup, 0, null);
        api.RenderPassEncoderSetBindGroup(pass, 1, blitBindGroup, 0, null);
        blitQuad.Draw(pass);

        backend.RenderDrawData(ImGui.GetDrawData(), pass);
        api.RenderPassEncoderEnd(pass);
        api.RenderPassEncoderRelease(pass);

        CommandBuffer* cmds = api.CommandEncoderFinish(enc, default(CommandBufferDescriptor));
        api.QueueSubmit(device.Queue, 1, &cmds);
        api.CommandBufferRelease(cmds);
        api.CommandEncoderRelease(enc);
        api.TextureViewRelease(target);
        device.Present();
    }

    private static void UploadDemoUniforms(WgpuPipeline pipeline, long startTick, uint width, uint height)
    {
        float t = (Environment.TickCount64 - startTick) / 1000.0f;
        float aspect = (float)width / Math.Max(1u, height);
        Matrix4x4 mv = Matrix4x4.CreateRotationY(t * 0.7f) * Matrix4x4.CreateRotationX(t * 0.5f) * Matrix4x4.CreateTranslation(0, 0, -2.5f);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspect, 0.1f, 100);
        proj.M33 = proj.M33 * 0.5f + 0.5f * proj.M43;
        proj.M34 *= 0.5f;

        Span<float> data = [mv.M11, mv.M12, mv.M13, mv.M14, mv.M21, mv.M22, mv.M23, mv.M24, mv.M31, mv.M32, mv.M33, mv.M34, mv.M41, mv.M42, mv.M43, mv.M44, proj.M11, proj.M12, proj.M13, proj.M14, proj.M21, proj.M22, proj.M23, proj.M24, proj.M31, proj.M32, proj.M33, proj.M34, proj.M41, proj.M42, proj.M43, proj.M44];
        fixed (float* p = data) { pipeline.UploadUniforms(p, 128); }
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
                "Phase 4: a textured cube sampling a 16×16 checkerboard. Three Tessellator "
                + "attributes (position, color, texcoord), two bind groups (uniforms + "
                + "texture/sampler), all working.");
        }
        ImGui.End();
    }
}
