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

    // ---------- Chunk (terrain) demo ------------------------------------------------

    /// <summary>Padded ChunkVertex (20 bytes). The real ChunkVertex is 18 bytes with 3×short
    /// position; WGSL has no Sint16x3, so 2 bytes of padding widen it to Sint16x4.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 2, Size = 22)]
    private struct ChunkDemoVertex(
        short x, short y, short z, ushort u, ushort v,
        uint color, byte skyLight, byte blockLight, ushort arrayLayer)
    {
        public int Color = (int)color;  // offset 0, Unorm8x4
        public short X = x, Y = y, Z = z; // offset 4
        public short Pad;                 // offset 10, unused
        public ushort U = u, V = v;       // offset 12, Uint16x2
        public byte SkyLight = skyLight;   // offset 16
        public byte BlockLight = blockLight; // offset 17
        public ushort ArrayLayer = arrayLayer; // offset 18
        public ushort Pad2;               // offset 20, Uint16x2 needs 4 bytes

        public static readonly uint Stride = 22;
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
        using WgpuFramebuffer fb = WgpuFramebuffer.CreateColorDepth(device,
            (uint)Display.getFramebufferWidth(), (uint)Display.getFramebufferHeight(),
            device.SurfaceFormat);
        CreateBlit(device, fb, out WgpuPipeline blitPipeline, out WgpuMesh blitQuad, out BindGroup* blitBindGroup);

        // Chunk pipeline + terrain array + demo mesh.
        CreateTerrainArray(device, out WgpuTextureArray terrainArray);
        using WgpuPipeline chunkPipeline = CreateChunkPipeline(device, terrainArray);
        using WgpuMesh terrain = CreateTerrainMesh(device);

        long startTick = Environment.TickCount64;

        try
        {
            while (!Display.isCloseRequested())
            {
                Display.processMessages();
                Resize(device);
                DrawFrame(device, backend, fb, blitPipeline, blitQuad, blitBindGroup,
                    chunkPipeline, terrainArray, terrain, startTick);
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
        if (w != device.Width || h != device.Height) device.Configure(w, h);
    }

    // ---------- Terrain texture array (procedural, 4 layers) ------------------------

    private static void CreateTerrainArray(WebGpuDevice device, out WgpuTextureArray array)
    {
        // Bind group layout for the array + sampler at group 1.
        Silk.NET.WebGPU.WebGPU api = device.Api;
        BindGroupLayoutEntry* te = stackalloc BindGroupLayoutEntry[2];
        te[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2DArray },
        };
        te[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        BindGroupLayoutDescriptor tbgl = new() { EntryCount = 2, Entries = te };
        BindGroupLayout* texBgl = api.DeviceCreateBindGroupLayout(device.Device, in tbgl);

        array = new WgpuTextureArray(device, 16, 4, texBgl);
        api.BindGroupLayoutRelease(texBgl); // WgpuTextureArray holds its own copy.

        // 4 layers, each a 16×16 checkerboard with a different pattern.
        for (uint layer = 0; layer < 4; layer++)
        {
            byte[] pixels = new byte[16 * 16 * 4];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int i = (y * 16 + x) * 4;
                    // Each layer gets a distinct color pair.
                    (byte r, byte g, byte b) = layer switch
                    {
                        0 => ((byte)200, (byte)100, (byte)50),  // brown (dirt)
                        1 => ((byte)100, (byte)200, (byte)50),  // green (grass top)
                        2 => ((byte)120, (byte)120, (byte)120), // grey (stone)
                        3 => ((byte)200, (byte)200, (byte)200), // white
                        _ => ((byte)255, (byte)0, (byte)255),   // magenta
                    };
                    bool dark = ((x ^ y ^ (int)layer) & 1) == 0;
                    float scale = dark ? 0.7f : 1.0f;
                    pixels[i + 0] = (byte)(r * scale);
                    pixels[i + 1] = (byte)(g * scale);
                    pixels[i + 2] = (byte)(b * scale);
                    pixels[i + 3] = 255;
                }
            array.UploadLayer(layer, pixels);
        }
    }

    // ---------- Chunk pipeline ------------------------------------------------------

    private static WgpuPipeline CreateChunkPipeline(WebGpuDevice device, WgpuTextureArray terrainArray)
    {
        string source = AssetManager.Instance.getAsset("shaders/chunk.wgsl").GetTextContent();
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
            ShaderModuleDescriptor sd = default; sd.NextInChain = (ChainedStruct*)&wgslDesc;
            module = api.DeviceCreateShaderModule(device.Device, in sd);
        }
        finally { SilkMarshal.Free((nint)code); }

        // Group 0: uniforms (large — all the chunk parameters).
        BindGroupLayoutEntry ue = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 256 },
        };
        BindGroupLayoutDescriptor ubgl = new() { EntryCount = 1, Entries = &ue };
        BindGroupLayout* uniformBgl = api.DeviceCreateBindGroupLayout(device.Device, in ubgl);

        // Group 1: texture array + sampler.
        BindGroupLayoutEntry* te = stackalloc BindGroupLayoutEntry[2];
        te[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2DArray },
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

        // 5 vertex attributes matching ChunkDemoVertex.
        VertexAttribute* attrs = stackalloc VertexAttribute[5];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Sint16x4, Offset = 4, ShaderLocation = 0 };   // X,Y,Z,pad
        attrs[1] = new VertexAttribute { Format = VertexFormat.Uint16x2, Offset = 12, ShaderLocation = 1 };  // U,V
        attrs[2] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 0, ShaderLocation = 2 };   // Color
        attrs[3] = new VertexAttribute { Format = VertexFormat.Uint8x2, Offset = 16, ShaderLocation = 3 };   // light
        attrs[4] = new VertexAttribute { Format = VertexFormat.Uint16x2, Offset = 18, ShaderLocation = 4 };     // arrayLayer (vec2<u32>, .x read)
        VertexBufferLayout vb = new()
        {
            ArrayStride = ChunkDemoVertex.Stride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 5,
            Attributes = attrs,
        };

        BlendComponent id = new() { Operation = BlendOperation.Add, SrcFactor = BlendFactor.One, DstFactor = BlendFactor.Zero };
        BlendState blend = new() { Color = id, Alpha = id };
        ColorTargetState ct = new() { Format = device.SurfaceFormat, Blend = &blend, WriteMask = ColorWriteMask.All };

        DepthStencilState ds = new()
        {
            Format = WgpuFramebuffer.DepthFormat,
            DepthWriteEnabled = true,
            DepthCompare = CompareFunction.LessEqual,
            StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
            StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
        };

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
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = Silk.NET.WebGPU.CullMode.Back },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                DepthStencil = &ds,
                Fragment = &fg,
            });
        }
        finally { SilkMarshal.Free((nint)vs); SilkMarshal.Free((nint)fs); }

        nuint us = 256;
        BufferDescriptor bd = new() { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)us };
        WgpuBuffer* ubuf = api.DeviceCreateBuffer(device.Device, in bd);
        BindGroupEntry bge = new() { Binding = 0, Buffer = ubuf, Offset = 0, Size = (ulong)us };
        BindGroupDescriptor bgd = new() { Layout = uniformBgl, EntryCount = 1, Entries = &bge };
        BindGroup* ubg = api.DeviceCreateBindGroup(device.Device, in bgd);

        return new WgpuPipeline(module, uniformBgl, texBgl, layout, rp, ubuf, ubg, device);
    }

    // ---------- Demo terrain mesh (flat plane, 4 tiles) ----------------------------

    private static WgpuMesh CreateTerrainMesh(WebGpuDevice device)
    {
        // A flat 2×2 plane of tiles at y=0, each with a different array layer.
        ChunkDemoVertex[] v = new ChunkDemoVertex[24]; // 4 tiles × 6 verts
        sbyte[] layerForTile = [0, 1, 2, 3];
        const ushort uvFull = 32767;
        const byte fullLight = 60;

        int vi = 0;
        for (int tileZ = 0; tileZ < 2; tileZ++)
            for (int tileX = 0; tileX < 2; tileX++)
            {
                ushort layer = (ushort)layerForTile[tileZ * 2 + tileX];
                float bx = tileX * 2.0f - 2.0f; // center the 2×2 at origin
                float bz = tileZ * 2.0f - 2.0f;

                // Position packed as short * 32767/64.
                short S(float f) => (short)(f * 32767.0f / 64.0f);

                v[vi + 0] = new(S(bx), S(0), S(bz), 0, 0, 0xFFFFFFFF, fullLight, fullLight, layer);
                v[vi + 1] = new(S(bx + 2), S(0), S(bz), uvFull, 0, 0xFFFFFFFF, fullLight, fullLight, layer);
                v[vi + 2] = new(S(bx + 2), S(0), S(bz + 2), uvFull, uvFull, 0xFFFFFFFF, fullLight, fullLight, layer);
                v[vi + 3] = new(S(bx + 2), S(0), S(bz + 2), uvFull, uvFull, 0xFFFFFFFF, fullLight, fullLight, layer);
                v[vi + 4] = new(S(bx), S(0), S(bz + 2), 0, uvFull, 0xFFFFFFFF, fullLight, fullLight, layer);
                v[vi + 5] = new(S(bx), S(0), S(bz), 0, 0, 0xFFFFFFFF, fullLight, fullLight, layer);
                vi += 6;
            }

        return new WgpuMesh(device, MemoryMarshal.AsBytes(v.AsSpan()), ChunkDemoVertex.Stride);
    }

    // ---------- Blit ------------------------------------------------------------------

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
            ShaderModuleDescriptor sd = default; sd.NextInChain = (ChainedStruct*)&wgslDesc;
            module = api.DeviceCreateShaderModule(device.Device, in sd);
        }
        finally { SilkMarshal.Free((nint)code); }

        BindGroupLayoutEntry ue = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 64 },
        };
        BindGroupLayoutDescriptor ubgl = new() { EntryCount = 1, Entries = &ue };
        BindGroupLayout* uniformBgl = api.DeviceCreateBindGroupLayout(device.Device, in ubgl);

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

        BufferDescriptor bd = new() { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = 64 };
        WgpuBuffer* ubuf = api.DeviceCreateBuffer(device.Device, in bd);
        BindGroupEntry bge = new() { Binding = 0, Buffer = ubuf, Offset = 0, Size = 64 };
        BindGroupDescriptor bgd = new() { Layout = uniformBgl, EntryCount = 1, Entries = &bge };
        BindGroup* ubg = api.DeviceCreateBindGroup(device.Device, in bgd);

        pipeline = new WgpuPipeline(module, uniformBgl, texBgl, layout, rp, ubuf, ubg, device);

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
        bindGroup = api.DeviceCreateBindGroup(device.Device, in bgDesc);

        BlitVertex[] quadVerts =
        [
            new(new(-1, -1, 0), new(0, 0)), new(new(1, -1, 0), new(1, 0)), new(new(1, 1, 0), new(1, 1)),
            new(new(1, 1, 0), new(1, 1)), new(new(-1, 1, 0), new(0, 1)), new(new(-1, -1, 0), new(0, 0)),
        ];
        quad = new WgpuMesh(device, MemoryMarshal.AsBytes(quadVerts.AsSpan()), BlitVertex.Stride);
    }

    // ---------- Frame -----------------------------------------------------------------

    private static void DrawFrame(
        WebGpuDevice device, ImGuiWgpuBackend backend,
        WgpuFramebuffer fb, WgpuPipeline blitPipeline, WgpuMesh blitQuad, BindGroup* blitBindGroup,
        WgpuPipeline chunkPipeline, WgpuTextureArray terrainArray, WgpuMesh terrain, long startTick)
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

        // Pass 1: terrain into the framebuffer with depth.
        RenderPassEncoder* offPass = fb.BeginPass(enc, s_clearColor);
        UploadChunkUniforms(chunkPipeline, startTick, device.Width, device.Height);
        api.RenderPassEncoderSetPipeline(offPass, chunkPipeline.Pipeline);
        api.RenderPassEncoderSetBindGroup(offPass, 0, chunkPipeline.UniformBindGroup, 0, null);
        api.RenderPassEncoderSetBindGroup(offPass, 1, terrainArray.BindGroup, 0, null);
        terrain.Draw(offPass);
        api.RenderPassEncoderEnd(offPass);
        api.RenderPassEncoderRelease(offPass);

        // Pass 2: blit + ImGui.
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

    private static void UploadChunkUniforms(WgpuPipeline pipeline, long startTick, uint width, uint height)
    {
        float t = (Environment.TickCount64 - startTick) / 1000.0f;
        float aspect = (float)width / Math.Max(1u, height);

        Matrix4x4 mv = Matrix4x4.CreateRotationX(-0.3f) * Matrix4x4.CreateTranslation(0, -2, -8);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspect, 0.1f, 200);
        proj.M33 = proj.M33 * 0.5f + 0.5f * proj.M43;
        proj.M34 *= 0.5f;

        // Write the Uniforms struct manually: modelView(64) + projection(64) + chunkPos(8)
        // + time(12) + ambientDarkness(4) + luminanceOffset(4) + wavy params + fog params.
        Span<float> data = stackalloc float[64]; // 256 bytes
        int off = 0;
        WriteMat4(data, ref off, mv);
        WriteMat4(data, ref off, proj);
        data[off++] = 0; data[off++] = 0; // chunkPos (0,0)
        data[off++] = t; data[off++] = 0; data[off++] = 0; // time (t, 0, 0)
        data[off++] = 0.0f; // ambientDarkness
        data[off++] = 0.05f; // luminanceOffset
        data[off++] = 0.0f; data[off++] = 0.0f; data[off++] = 0.0f; data[off++] = 0.0f; // wavy strengths/speeds
        data[off++] = 0; // wavyPlantMode
        for (int i = 0; i < 8; i++) data[off++] = 0; // wavyLeafLayers
        data[off++] = 0; // wavyLeafCount
        for (int i = 0; i < 8; i++) data[off++] = 0; // wavyPlantLayers
        data[off++] = 0; // wavyPlantCount
        data[off++] = 0.5f; data[off++] = 0.5f; data[off++] = 0.6f; data[off++] = 1.0f; // fogColor
        data[off++] = 5.0f; data[off++] = 80.0f; data[off++] = 0.01f; // fogStart, fogEnd, fogDensity
        data[off++] = 0; // fogMode = linear
        data[off++] = 0; // chunkFadeEnabled = false
        data[off++] = 1.0f; // fadeProgress

        fixed (float* p = data) { pipeline.UploadUniforms(p, 256); }
    }

    private static void WriteMat4(Span<float> data, ref int off, Matrix4x4 m)
    {
        data[off++] = m.M11; data[off++] = m.M12; data[off++] = m.M13; data[off++] = m.M14;
        data[off++] = m.M21; data[off++] = m.M22; data[off++] = m.M23; data[off++] = m.M24;
        data[off++] = m.M31; data[off++] = m.M32; data[off++] = m.M33; data[off++] = m.M34;
        data[off++] = m.M41; data[off++] = m.M42; data[off++] = m.M43; data[off++] = m.M44;
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
                "Phase 6: a 2×2 patch of terrain tiles drawn through chunk.wgsl with a "
                + "texture array (4 layers, 16×16 each). All 5 ChunkVertex attributes, the "
                + "brightness ramp, packed short positions, and array-layer sampling work.");
        }
        ImGui.End();
    }
}
