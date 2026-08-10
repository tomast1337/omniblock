using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     Draws <see cref="ImDrawData" /> through WebGPU.
/// </summary>
/// <remarks>
///     <para>
///         Hexa.NET.ImGui ships an OpenGL3 backend and no WebGPU one, so this stands in for
///         <c>ImGuiImplOpenGL3</c>: font and image textures, per-frame geometry upload, and one
///         scissored indexed draw per <see cref="ImDrawCmd" />. <c>ImGuiImplGLFW</c> is unaffected
///         — it only feeds input events and touches no graphics API.
///     </para>
///     <para>
///         Textures are ImGui's to own in this version: it hands out <see cref="ImTextureData" />
///         with a status saying what it wants done, and the backend answers by creating, updating
///         or destroying and writing its own handle back. That is why there is no font atlas upload
///         at startup — the atlas arrives as a texture request like any other.
///     </para>
/// </remarks>
public sealed unsafe class ImGuiWgpuBackend : IDisposable
{
    private readonly WebGpuDevice _device;
    private readonly Silk.NET.WebGPU.WebGPU _api;

    private readonly ShaderModule* _shaderModule;
    private readonly BindGroupLayout* _uniformLayout;
    private readonly BindGroupLayout* _textureLayout;
    private readonly PipelineLayout* _pipelineLayout;
    private readonly RenderPipeline* _pipeline;
    private readonly WgpuBuffer* _uniformBuffer;
    private readonly BindGroup* _uniformBindGroup;
    private readonly Sampler* _sampler;

    private WgpuBuffer* _vertexBuffer;
    private WgpuBuffer* _indexBuffer;
    private ulong _vertexCapacity;
    private ulong _indexCapacity;

    private byte[] _vertexScratch = [];
    private byte[] _indexScratch = [];

    private readonly Dictionary<ulong, BackendTexture> _textures = [];
    private ulong _nextTextureId = 1;

    private bool _disposed;

    private readonly struct BackendTexture(Texture* texture, TextureView* view, BindGroup* bindGroup)
    {
        public Texture* Texture { get; } = texture;
        public TextureView* View { get; } = view;
        public BindGroup* BindGroup { get; } = bindGroup;
    }

    /// <summary>One <c>mat4x4&lt;f32&gt;</c>, and the smallest a uniform binding is allowed to be.</summary>
    private const ulong UniformSize = 64;

    private static readonly uint s_vertexStride = (uint)sizeof(ImDrawVert);

    public ImGuiWgpuBackend(WebGpuDevice device)
    {
        _device = device;
        _api = device.Api;

        // Without this ImGui keeps the legacy "backend uploads the font atlas once" contract and
        // never emits texture requests, so nothing has pixels.
        ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasTextures | ImGuiBackendFlags.RendererHasVtxOffset;

        _shaderModule = CreateShaderModule();
        CreateBindGroupLayouts(out _uniformLayout, out _textureLayout);
        _pipelineLayout = CreatePipelineLayout();
        _pipeline = CreatePipeline();
        _sampler = CreateSampler();
        CreateUniforms(out _uniformBuffer, out _uniformBindGroup);
    }

    /// <summary>Draws one frame's ImGui output into an already-begun render pass.</summary>
    public void RenderDrawData(ImDrawDataPtr drawData, RenderPassEncoder* pass)
    {
        // Texture creation and queue writes are ordered ahead of the submit that carries this
        // pass, so servicing requests here rather than before the pass began is still in time.
        ServiceTextureRequests(drawData);

        int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (framebufferWidth <= 0 || framebufferHeight <= 0 || drawData.TotalVtxCount == 0)
        {
            return;
        }

        UploadGeometry(drawData);
        UploadProjection(drawData);

        _api.RenderPassEncoderSetPipeline(pass, _pipeline);
        _api.RenderPassEncoderSetBindGroup(pass, 0, _uniformBindGroup, 0, null);
        _api.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, _vertexCapacity);
        _api.RenderPassEncoderSetIndexBuffer(pass, _indexBuffer, IndexFormat.Uint16, 0, _indexCapacity);

        Vector2 clipOffset = drawData.DisplayPos;
        Vector2 clipScale = drawData.FramebufferScale;

        int vertexOffset = 0;
        uint indexOffset = 0;

        for (int list = 0; list < drawData.CmdListsCount; list++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[list];

            for (int i = 0; i < cmdList.CmdBuffer.Size; i++)
            {
                ImDrawCmd cmd = cmdList.CmdBuffer[i];
                if (cmd.UserCallback is not null || cmd.ElemCount == 0)
                {
                    continue;
                }

                // ImGui's clip rects are in ImGui space and may run past the framebuffer on either
                // side; WebGPU rejects a scissor that does, rather than clamping it as GL did.
                int minX = (int)((cmd.ClipRect.X - clipOffset.X) * clipScale.X);
                int minY = (int)((cmd.ClipRect.Y - clipOffset.Y) * clipScale.Y);
                int maxX = (int)((cmd.ClipRect.Z - clipOffset.X) * clipScale.X);
                int maxY = (int)((cmd.ClipRect.W - clipOffset.Y) * clipScale.Y);

                minX = Math.Clamp(minX, 0, framebufferWidth);
                minY = Math.Clamp(minY, 0, framebufferHeight);
                maxX = Math.Clamp(maxX, 0, framebufferWidth);
                maxY = Math.Clamp(maxY, 0, framebufferHeight);

                if (maxX <= minX || maxY <= minY)
                {
                    continue;
                }

                if (!_textures.TryGetValue(cmd.TexRef.GetTexID().Handle, out BackendTexture texture))
                {
                    continue;
                }

                _api.RenderPassEncoderSetBindGroup(pass, 1, texture.BindGroup, 0, null);
                _api.RenderPassEncoderSetScissorRect(pass, (uint)minX, (uint)minY, (uint)(maxX - minX), (uint)(maxY - minY));
                _api.RenderPassEncoderDrawIndexed(
                    pass,
                    cmd.ElemCount,
                    1,
                    indexOffset + cmd.IdxOffset,
                    vertexOffset + (int)cmd.VtxOffset,
                    0);
            }

            vertexOffset += cmdList.VtxBuffer.Size;
            indexOffset += (uint)cmdList.IdxBuffer.Size;
        }
    }

    /// <summary>Answers every texture request ImGui made this frame.</summary>
    private void ServiceTextureRequests(ImDrawDataPtr drawData)
    {
        ImVector<ImTextureDataPtr>* requests = drawData.Handle->Textures;
        if (requests is null)
        {
            return;
        }

        for (int i = 0; i < requests->Size; i++)
        {
            ImTextureDataPtr request = (*requests)[i];
            switch (request.Status)
            {
                case ImTextureStatus.WantCreate:
                    CreateTexture(request);
                    break;

                case ImTextureStatus.WantUpdates:
                    UpdateTexture(request);
                    break;

                case ImTextureStatus.WantDestroy when request.UnusedFrames > 0:
                    DestroyTexture(request);
                    break;
            }
        }
    }

    private void CreateTexture(ImTextureDataPtr request)
    {
        TextureDescriptor descriptor = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D((uint)request.Width, (uint)request.Height, 1),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        Texture* texture = _api.DeviceCreateTexture(_device.Device, in descriptor);

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = descriptor.Format,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        TextureView* view = _api.TextureCreateView(texture, in viewDescriptor);

        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };

        BindGroupDescriptor bindGroupDescriptor = new()
        {
            Layout = _textureLayout,
            EntryCount = 2,
            Entries = entries,
        };

        BindGroup* bindGroup = _api.DeviceCreateBindGroup(_device.Device, in bindGroupDescriptor);

        ulong id = _nextTextureId++;
        _textures[id] = new BackendTexture(texture, view, bindGroup);

        request.SetTexID(new ImTextureID(id));
        request.SetStatus(ImTextureStatus.Ok);

        WriteRegion(texture, request, 0, 0, request.Width, request.Height);
    }

    private void UpdateTexture(ImTextureDataPtr request)
    {
        if (!_textures.TryGetValue(request.GetTexID().Handle, out BackendTexture texture))
        {
            return;
        }

        ImTextureRect rect = request.UpdateRect;
        WriteRegion(texture.Texture, request, rect.X, rect.Y, rect.W, rect.H);
        request.SetStatus(ImTextureStatus.Ok);
    }

    /// <summary>
    ///     Uploads a sub-rectangle of ImGui's pixels.
    /// </summary>
    /// <remarks>
    ///     The source rows are a window onto the full-width atlas rather than a packed copy of the
    ///     rectangle, so the row stride stays the atlas pitch and the starting pixel carries the
    ///     offset.
    /// </remarks>
    private void WriteRegion(Texture* texture, ImTextureDataPtr request, int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ImageCopyTexture destination = new()
        {
            Texture = texture,
            MipLevel = 0,
            Origin = new Origin3D((uint)x, (uint)y, 0),
            Aspect = TextureAspect.All,
        };

        uint pitch = (uint)request.GetPitch();

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = pitch,
            RowsPerImage = (uint)height,
        };

        Extent3D extent = new((uint)width, (uint)height, 1);

        // The last row is the only one that need not be a full pitch, but wgpu reads
        // BytesPerRow * (height - 1) + widthInBytes, so handing it the whole span is safe.
        nuint size = (nuint)(pitch * (uint)height);

        _api.QueueWriteTexture(_device.Queue, in destination, request.GetPixelsAt(x, y), size, in layout, in extent);
    }

    private void DestroyTexture(ImTextureDataPtr request)
    {
        ulong id = request.GetTexID().Handle;
        if (_textures.Remove(id, out BackendTexture texture))
        {
            Release(texture);
        }

        request.SetTexID(ImTextureID.Null);
        request.SetStatus(ImTextureStatus.Destroyed);
    }

    /// <summary>
    ///     Registers a texture view this backend does not own — e.g. an offscreen framebuffer's
    ///     colour attachment — under a stable id usable as an <see cref="ImTextureID" />, so
    ///     something rendered outside ImGui's own texture-request flow (the F3 debug viewport's
    ///     world preview) can still be drawn with <c>ImGui.Image</c>.
    /// </summary>
    /// <remarks>
    ///     The caller keeps ownership of <paramref name="view" />: only the bind group wrapping it
    ///     is released, whether by <see cref="UnregisterExternalTexture" /> or by
    ///     <see cref="Dispose" />. Call <see cref="UpdateExternalTexture" />, not this again, once
    ///     the caller recreates the view (e.g. on resize) — see that method for why.
    /// </remarks>
    public ulong RegisterExternalTexture(TextureView* view)
    {
        BindGroup* bindGroup = CreateExternalBindGroup(view);
        ulong id = _nextTextureId++;
        // Texture/View left null: that is how Release tells an external entry apart from one it
        // created itself and must destroy.
        _textures[id] = new BackendTexture(null, null, bindGroup);
        return id;
    }

    /// <summary>
    ///     Repoints an id from <see cref="RegisterExternalTexture" /> at a newly-recreated view,
    ///     keeping the same id rather than minting a new one.
    /// </summary>
    /// <remarks>
    ///     This has to keep the id stable rather than have the caller unregister-and-re-register,
    ///     because <c>ImGui.Render()</c> runs a frame behind the frame that submits its draw data
    ///     (see <see cref="WebGpuGameRenderer.ImguiOpen" />): the draw commands about to be
    ///     submitted this frame were built last frame, against whatever id was current then. A
    ///     resize that swaps in a new id every time the caller's view changes — which, for an
    ///     ImGui-docked panel, is nearly every frame from sub-pixel layout jitter alone — means
    ///     those commands permanently reference an id one frame too old to still exist, so the
    ///     lookup in <see cref="RenderDrawData" /> misses and the draw is silently skipped forever.
    ///     Updating the same id in place instead means a stale reference from last frame's draw
    ///     data still resolves, to whatever the id points at now.
    /// </remarks>
    public void UpdateExternalTexture(ulong id, TextureView* view)
    {
        if (id == 0 || !_textures.TryGetValue(id, out BackendTexture old))
        {
            return;
        }

        BindGroup* bindGroup = CreateExternalBindGroup(view);
        _api.BindGroupRelease(old.BindGroup);
        _textures[id] = new BackendTexture(null, null, bindGroup);
    }

    private BindGroup* CreateExternalBindGroup(TextureView* view)
    {
        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };

        byte* label = (byte*)SilkMarshal.StringToPtr("ImGui.ExternalTexture");
        BindGroupDescriptor descriptor = new()
        {
            Label = label,
            Layout = _textureLayout,
            EntryCount = 2,
            Entries = entries,
        };

        BindGroup* group = _api.DeviceCreateBindGroup(_device.Device, in descriptor);
        SilkMarshal.Free((nint)label);
        return group;
    }

    /// <summary>Releases the bind group created by <see cref="RegisterExternalTexture" />. A no-op for id 0.</summary>
    public void UnregisterExternalTexture(ulong id)
    {
        if (id != 0 && _textures.Remove(id, out BackendTexture texture))
        {
            Release(texture);
        }
    }

    private void Release(BackendTexture texture)
    {
        _api.BindGroupRelease(texture.BindGroup);

        // Null for an externally-owned texture registered through RegisterExternalTexture — the
        // caller owns that view and, if it exists, the texture behind it.
        if (texture.View is not null) _api.TextureViewRelease(texture.View);
        if (texture.Texture is not null)
        {
            _api.TextureDestroy(texture.Texture);
            _api.TextureRelease(texture.Texture);
        }
    }

    /// <summary>Concatenates every command list's vertices and indices into one buffer each.</summary>
    /// <remarks>
    ///     A draw then addresses its list through the base-vertex and first-index arguments, which
    ///     is why <see cref="ImGuiBackendFlags.RendererHasVtxOffset" /> is declared: without it
    ///     ImGui splits lists to keep indices under 16 bits itself.
    /// </remarks>
    private void UploadGeometry(ImDrawDataPtr drawData)
    {
        ulong vertexBytes = Align4((ulong)drawData.TotalVtxCount * s_vertexStride);
        ulong indexBytes = Align4((ulong)drawData.TotalIdxCount * sizeof(ushort));

        EnsureBuffer(ref _vertexBuffer, ref _vertexCapacity, vertexBytes, BufferUsage.Vertex);
        EnsureBuffer(ref _indexBuffer, ref _indexCapacity, indexBytes, BufferUsage.Index);

        // Packed on the CPU first, and written once each, because a queue write must be a whole
        // number of 4-byte words: an odd index count in one list would end the next list's write
        // on an odd offset, and every write after it would be rejected.
        EnsureScratch(ref _vertexScratch, vertexBytes);
        EnsureScratch(ref _indexScratch, indexBytes);

        ulong vertexOffset = 0;
        ulong indexOffset = 0;

        fixed (byte* vertexScratch = _vertexScratch)
        fixed (byte* indexScratch = _indexScratch)
        {
            for (int list = 0; list < drawData.CmdListsCount; list++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[list];

                ulong listVertexBytes = (ulong)cmdList.VtxBuffer.Size * s_vertexStride;
                ulong listIndexBytes = (ulong)cmdList.IdxBuffer.Size * sizeof(ushort);

                System.Buffer.MemoryCopy(
                    cmdList.VtxBuffer.Data, vertexScratch + vertexOffset, vertexBytes - vertexOffset, listVertexBytes);
                System.Buffer.MemoryCopy(
                    cmdList.IdxBuffer.Data, indexScratch + indexOffset, indexBytes - indexOffset, listIndexBytes);

                vertexOffset += listVertexBytes;
                indexOffset += listIndexBytes;
            }

            _api.QueueWriteBuffer(_device.Queue, _vertexBuffer, 0, vertexScratch, (nuint)vertexBytes);
            _api.QueueWriteBuffer(_device.Queue, _indexBuffer, 0, indexScratch, (nuint)indexBytes);
        }
    }

    private static void EnsureScratch(ref byte[] scratch, ulong needed)
    {
        if ((ulong)scratch.Length < needed)
        {
            scratch = new byte[needed];
        }
    }

    private void EnsureBuffer(ref WgpuBuffer* buffer, ref ulong capacity, ulong needed, BufferUsage usage)
    {
        if (buffer is not null && capacity >= needed)
        {
            return;
        }

        if (buffer is not null)
        {
            _api.BufferDestroy(buffer);
            _api.BufferRelease(buffer);
        }

        // Overshoot so a frame that grows by a few glyphs does not reallocate.
        capacity = Math.Max(Align4(needed + (needed / 2)), 4096);

        BufferDescriptor descriptor = new()
        {
            Usage = usage | BufferUsage.CopyDst,
            Size = capacity,
            MappedAtCreation = false,
        };

        buffer = _api.DeviceCreateBuffer(_device.Device, in descriptor);
    }

    /// <summary>
    ///     Screen space to clip space, with Z in WebGPU's [0, 1] rather than OpenGL's [-1, 1].
    /// </summary>
    /// <remarks>
    ///     Every vertex leaves the shader at Z = 0, so the third column only has to keep that
    ///     inside the depth range. Y is negated because ImGui counts pixels down from the top and
    ///     clip space counts up.
    /// </remarks>
    private void UploadProjection(ImDrawDataPtr drawData)
    {
        float left = drawData.DisplayPos.X;
        float right = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float top = drawData.DisplayPos.Y;
        float bottom = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        // Column-major, as WGSL reads a mat4x4.
        Span<float> projection =
        [
            2.0f / (right - left), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (top - bottom), 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0.0f, 1.0f,
        ];

        fixed (float* data = projection)
        {
            _api.QueueWriteBuffer(_device.Queue, _uniformBuffer, 0, data, (nuint)UniformSize);
        }
    }

    private ShaderModule* CreateShaderModule()
    {
        string source = AssetManager.Instance.GetAsset("shaders/imgui.wgsl").GetTextContent();
        byte* code = (byte*)SilkMarshal.StringToPtr(source);

        try
        {
            ShaderModuleWGSLDescriptor wgsl = new()
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };

            ShaderModuleDescriptor descriptor = new() { NextInChain = (ChainedStruct*)&wgsl };
            return _api.DeviceCreateShaderModule(_device.Device, in descriptor);
        }
        finally
        {
            SilkMarshal.Free((nint)code);
        }
    }

    private void CreateBindGroupLayouts(out BindGroupLayout* uniformLayout, out BindGroupLayout* textureLayout)
    {
        BindGroupLayoutEntry uniformEntry = new()
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                MinBindingSize = UniformSize,
            },
        };

        BindGroupLayoutDescriptor uniformDescriptor = new() { EntryCount = 1, Entries = &uniformEntry };
        uniformLayout = _api.DeviceCreateBindGroupLayout(_device.Device, in uniformDescriptor);

        BindGroupLayoutEntry* textureEntries = stackalloc BindGroupLayoutEntry[2];
        textureEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
            },
        };
        textureEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };

        BindGroupLayoutDescriptor textureDescriptor = new() { EntryCount = 2, Entries = textureEntries };
        textureLayout = _api.DeviceCreateBindGroupLayout(_device.Device, in textureDescriptor);
    }

    private PipelineLayout* CreatePipelineLayout()
    {
        BindGroupLayout** layouts = stackalloc BindGroupLayout*[2];
        layouts[0] = _uniformLayout;
        layouts[1] = _textureLayout;

        PipelineLayoutDescriptor descriptor = new()
        {
            BindGroupLayoutCount = 2,
            BindGroupLayouts = layouts,
        };

        return _api.DeviceCreatePipelineLayout(_device.Device, in descriptor);
    }

    private RenderPipeline* CreatePipeline()
    {
        VertexAttribute* attributes = stackalloc VertexAttribute[3];
        attributes[0] = new VertexAttribute
        {
            Format = VertexFormat.Float32x2,
            Offset = (ulong)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.Pos)),
            ShaderLocation = 0,
        };
        attributes[1] = new VertexAttribute
        {
            Format = VertexFormat.Float32x2,
            Offset = (ulong)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.Uv)),
            ShaderLocation = 1,
        };
        attributes[2] = new VertexAttribute
        {
            Format = VertexFormat.Unorm8x4,
            Offset = (ulong)Marshal.OffsetOf<ImDrawVert>(nameof(ImDrawVert.Col)),
            ShaderLocation = 2,
        };

        VertexBufferLayout bufferLayout = new()
        {
            ArrayStride = s_vertexStride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 3,
            Attributes = attributes,
        };

        BlendState blend = new()
        {
            Color = new BlendComponent
            {
                Operation = BlendOperation.Add,
                SrcFactor = BlendFactor.SrcAlpha,
                DstFactor = BlendFactor.OneMinusSrcAlpha,
            },
            Alpha = new BlendComponent
            {
                Operation = BlendOperation.Add,
                SrcFactor = BlendFactor.One,
                DstFactor = BlendFactor.OneMinusSrcAlpha,
            },
        };

        ColorTargetState target = new()
        {
            Format = _device.SurfaceFormat,
            Blend = &blend,
            WriteMask = ColorWriteMask.All,
        };

        byte* vertexEntry = (byte*)SilkMarshal.StringToPtr("vs_main");
        byte* fragmentEntry = (byte*)SilkMarshal.StringToPtr("fs_main");

        try
        {
            FragmentState fragment = new()
            {
                Module = _shaderModule,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &target,
            };

            RenderPipelineDescriptor descriptor = new()
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState
                {
                    Module = _shaderModule,
                    EntryPoint = vertexEntry,
                    BufferCount = 1,
                    Buffers = &bufferLayout,
                },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = Silk.NET.WebGPU.CullMode.None,
                },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };

            return _api.DeviceCreateRenderPipeline(_device.Device, in descriptor);
        }
        finally
        {
            SilkMarshal.Free((nint)vertexEntry);
            SilkMarshal.Free((nint)fragmentEntry);
        }
    }

    private Sampler* CreateSampler()
    {
        SamplerDescriptor descriptor = new()
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0.0f,
            LodMaxClamp = 1.0f,
            MaxAnisotropy = 1,
        };

        return _api.DeviceCreateSampler(_device.Device, in descriptor);
    }

    private void CreateUniforms(out WgpuBuffer* buffer, out BindGroup* bindGroup)
    {
        BufferDescriptor bufferDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = UniformSize,
        };

        buffer = _api.DeviceCreateBuffer(_device.Device, in bufferDescriptor);

        BindGroupEntry entry = new()
        {
            Binding = 0,
            Buffer = buffer,
            Offset = 0,
            Size = UniformSize,
        };

        byte* label = (byte*)SilkMarshal.StringToPtr("ImGui.Uniform");
        BindGroupDescriptor descriptor = new()
        {
            Label = label,
            Layout = _uniformLayout,
            EntryCount = 1,
            Entries = &entry,
        };

        bindGroup = _api.DeviceCreateBindGroup(_device.Device, in descriptor);
        SilkMarshal.Free((nint)label);
    }

    private static ulong Align4(ulong size) => (size + 3UL) & ~3UL;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (BackendTexture texture in _textures.Values)
        {
            Release(texture);
        }

        _textures.Clear();

        if (_vertexBuffer is not null) _api.BufferRelease(_vertexBuffer);
        if (_indexBuffer is not null) _api.BufferRelease(_indexBuffer);

        _api.BindGroupRelease(_uniformBindGroup);
        _api.BufferRelease(_uniformBuffer);
        _api.SamplerRelease(_sampler);
        _api.RenderPipelineRelease(_pipeline);
        _api.PipelineLayoutRelease(_pipelineLayout);
        _api.BindGroupLayoutRelease(_textureLayout);
        _api.BindGroupLayoutRelease(_uniformLayout);
        _api.ShaderModuleRelease(_shaderModule);
    }
}
