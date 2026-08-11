using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     A compiled shader module plus the pipeline and bind-group layout built from it, including
///     the uniform buffer and bind group every slot program carries.
/// </summary>
/// <remarks>
///     <para>
///         There is one of these per <see cref="RenderState" /> + vertex-attrib-layout pair that
///         the slot program draws under. The uniform buffer is per-pipeline rather than per-frame
///         because <see cref="RenderState" /> affects only the pipeline descriptor, not what
///         reachable uniforms are. Two pipelines from the same shader module share the same
///         uniforms — they differ in blend, depth and cull, none of which is a shader input.
///     </para>
/// </remarks>
public sealed unsafe class WgpuPipeline : IDisposable
{
    public ShaderModule* Module { get; }
    /// <summary>The uniform bind group layout (group 0).</summary>
    public BindGroupLayout* BindGroupLayout { get; }

    /// <summary>The texture bind group layout (group 1), or null for untextured pipelines.</summary>
    public BindGroupLayout* TextureBindGroupLayout { get; }

    /// <summary>
    ///     The texture-array bind group layout (group 2), or null when the shader samples no array.
    /// </summary>
    /// <remarks>
    ///     A group of its own rather than more bindings in group 1 because the two textures have
    ///     different owners and different lifetimes: the 2D one changes with every bind, while the
    ///     array is rebuilt only when the pack is.
    /// </remarks>
    public BindGroupLayout* TextureArrayBindGroupLayout { get; }

    public PipelineLayout* Layout { get; }
    public RenderPipeline* Pipeline { get; }

    /// <summary>The uniform buffer, large enough for the full <c>Uniforms</c> struct.</summary>
    public WgpuBuffer* UniformBuffer { get; }

    /// <summary>The per-frame bind group binding the uniform buffer to the layout (group 0).</summary>
    public BindGroup* UniformBindGroup { get; }

    private readonly WebGpuDevice _device;

    /// <summary>
    ///     Spare uniform buffers and their bind groups, handed out one per draw by
    ///     <see cref="BindNextUniforms{T}" /> and reused from the top of the next frame.
    /// </summary>
    private readonly List<(nint Buffer, nint Group)> _uniformPool = [];
    private int _uniformPoolNext;
    private readonly uint _uniformSize;
    private bool _disposed;

    /// <summary>
    ///     Whether the group-0 layout declared its uniform binding with a dynamic offset — set from
    ///     the entries passed to the public constructor, not a separate flag, so it can never drift
    ///     out of sync with what the layout actually says.
    /// </summary>
    private readonly bool _dynamicUniformLayout;

    /// <summary>
    ///     One GPU buffer holding every draw's uniforms for the frame, written with a single
    ///     <c>QueueWriteBuffer</c> call and read back per draw via a dynamic offset — the batched
    ///     alternative to <see cref="_uniformPool" />'s one-buffer-per-draw approach, for call sites
    ///     with too many draws per frame for a write-per-draw to be free.
    /// </summary>
    private WgpuBuffer* _dynamicUniformBuffer;
    private BindGroup* _dynamicUniformBindGroup;
    private int _dynamicUniformCapacity;
    private byte[] _dynamicUniformStaging = [];

    /// <summary>
    ///     WebGPU's guaranteed baseline for <c>minUniformBufferOffsetAlignment</c> — every conformant
    ///     adapter supports at least this without querying device limits.
    /// </summary>
    private const uint DynamicUniformAlignment = 256;

    private uint DynamicUniformStride => AlignUp(_uniformSize, DynamicUniformAlignment);

    private static uint AlignUp(uint value, uint align) => (value + align - 1) / align * align;

    /// <summary>
    ///     Wraps an already-built pipeline.
    /// </summary>
    internal WgpuPipeline(
        ShaderModule* module,
        BindGroupLayout* bindGroupLayout,
        BindGroupLayout* textureBindGroupLayout,
        PipelineLayout* layout,
        RenderPipeline* pipeline,
        WgpuBuffer* uniformBuffer,
        BindGroup* uniformBindGroup,
        WebGpuDevice device)
    {
        _device = device;
        _uniformSize = 0;
        Module = module;
        BindGroupLayout = bindGroupLayout;
        TextureBindGroupLayout = textureBindGroupLayout;
        TextureArrayBindGroupLayout = null;
        Layout = layout;
        Pipeline = pipeline;
        UniformBuffer = uniformBuffer;
        UniformBindGroup = uniformBindGroup;
    }

    /// <summary>
    ///     Builds a shader module from WGSL source, creates the bind-group layouts from
    ///     <paramref name="uniformEntries"/> and optional <paramref name="textureEntries"/>,
    ///     and creates a pipeline matching <paramref name="state"/>, the vertex
    ///     <paramref name="buffers"/>, and the colour format.
    /// </summary>
    /// <param name="depthFormat">
    ///     The depth texture format, or <c>TextureFormat.Undefined</c> when the pass has no depth
    ///     attachment. Must match what <see cref="WgpuFramebuffer.BeginPass"/> attaches.
    /// </param>
    public WgpuPipeline(
        WebGpuDevice device,
        string wgslSource,
        string entryPoint,
        uint uniformSize,
        ReadOnlySpan<BindGroupLayoutEntry> uniformEntries,
        ReadOnlySpan<BindGroupLayoutEntry> textureEntries,
        VertexBufferLayout* buffers,
        nuint bufferCount,
        RenderState state,
        TextureFormat colorFormat,
        TextureFormat depthFormat = TextureFormat.Undefined,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList,
        ReadOnlySpan<BindGroupLayoutEntry> textureArrayEntries = default,
        string? label = null,
        string fragmentEntryPoint = "fs_main")
    {
        _device = device;
        _uniformSize = Math.Max(uniformSize, 64u);
        Silk.NET.WebGPU.WebGPU api = device.Api;

        Module = CreateShaderModule(api, device.Device, wgslSource);
        BindGroupLayout = CreateBindGroupLayout(api, device.Device, uniformEntries);
        TextureBindGroupLayout = textureEntries.Length > 0
            ? CreateBindGroupLayout(api, device.Device, textureEntries)
            : null;
        TextureArrayBindGroupLayout = textureArrayEntries.Length > 0
            ? CreateBindGroupLayout(api, device.Device, textureArrayEntries)
            : null;
        Layout = CreatePipelineLayout(api, device.Device,
            BindGroupLayout, TextureBindGroupLayout, TextureArrayBindGroupLayout);
        Pipeline = CreateRenderPipeline(api, device.Device, Module, entryPoint, fragmentEntryPoint, Layout, buffers, bufferCount, state, colorFormat, depthFormat, topology, label);
        CreateUniforms(api, device.Device, BindGroupLayout, uniformSize, out WgpuBuffer* ub, out BindGroup* ug);
        UniformBuffer = ub;
        UniformBindGroup = ug;

        _dynamicUniformLayout = uniformEntries.Length > 0 && uniformEntries[0].Buffer.HasDynamicOffset;
    }

    private static ShaderModule* CreateShaderModule(Silk.NET.WebGPU.WebGPU api, Device* device, string source)
    {
        byte* code = (byte*)SilkMarshal.StringToPtr(source);
        try
        {
            ShaderModuleWGSLDescriptor wgsl = new()
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };

            ShaderModuleDescriptor descriptor = default;
            descriptor.NextInChain = (ChainedStruct*)&wgsl;
            return api.DeviceCreateShaderModule(device, in descriptor);
        }
        finally
        {
            SilkMarshal.Free((nint)code);
        }
    }

    private static BindGroupLayout* CreateBindGroupLayout(
        Silk.NET.WebGPU.WebGPU api, Device* device, ReadOnlySpan<BindGroupLayoutEntry> entries)
    {
        fixed (BindGroupLayoutEntry* entriesPtr = entries)
        {
            BindGroupLayoutDescriptor descriptor = new()
            {
                EntryCount = (nuint)entries.Length,
                Entries = entriesPtr,
            };

            return api.DeviceCreateBindGroupLayout(device, in descriptor);
        }
    }

    private static PipelineLayout* CreatePipelineLayout(
        Silk.NET.WebGPU.WebGPU api, Device* device,
        BindGroupLayout* bindGroupLayout, BindGroupLayout* textureBindGroupLayout,
        BindGroupLayout* textureArrayBindGroupLayout)
    {
        // Consecutive from group 0, so a null texture group means there is no array group either.
        BindGroupLayout** layouts = stackalloc BindGroupLayout*[3];
        layouts[0] = bindGroupLayout;
        layouts[1] = textureBindGroupLayout;
        layouts[2] = textureArrayBindGroupLayout;

        nuint count = textureBindGroupLayout is null
            ? 1u
            : textureArrayBindGroupLayout is null ? 2u : 3u;

        PipelineLayoutDescriptor descriptor = new()
        {
            BindGroupLayoutCount = count,
            BindGroupLayouts = layouts,
        };

        return api.DeviceCreatePipelineLayout(device, in descriptor);
    }

    private static RenderPipeline* CreateRenderPipeline(
        Silk.NET.WebGPU.WebGPU api,
        Device* device,
        ShaderModule* module,
        string entryPoint,
        string fragmentEntryPoint,
        PipelineLayout* layout,
        VertexBufferLayout* buffers,
        nuint bufferCount,
        RenderState state,
        TextureFormat colorFormat,
        TextureFormat depthFormat,
        PrimitiveTopology topology,
        string? label = null)
    {
        byte* vertexEntry = (byte*)SilkMarshal.StringToPtr(entryPoint);
        byte* fragmentEntry = (byte*)SilkMarshal.StringToPtr(fragmentEntryPoint);
        byte* labelPtr = label is null ? null : (byte*)SilkMarshal.StringToPtr(label);

        try
        {
            BlendComponent colorBlend = BlendFor(state.Blend);
            BlendComponent alphaBlend = AlphaBlendFor(state.Blend);

            BlendState blend = new() { Color = colorBlend, Alpha = alphaBlend };

            ColorTargetState target = new()
            {
                Format = colorFormat,
                Blend = &blend,
                WriteMask = state.ColorWrite ? ColorWriteMask.All : ColorWriteMask.None,
            };

            Silk.NET.WebGPU.CullMode cullMode = state.Cull switch
            {
                CullMode.None => Silk.NET.WebGPU.CullMode.None,
                CullMode.Back => Silk.NET.WebGPU.CullMode.Back,
                _ => Silk.NET.WebGPU.CullMode.Back,
            };

            DepthStencilState depthStencil = default;
            DepthStencilState* pDepthStencil = null;

            // Whether the pass has a depth attachment, not whether this draw tests against it: a
            // pipeline with no depth-stencil state cannot be set on a pass that has one, and
            // rejecting the pipeline is not what "depth test off" means. GL's off is a test that
            // always passes and writes nothing, so that is what it becomes here.
            if (depthFormat != TextureFormat.Undefined)
            {
                depthStencil = new DepthStencilState
                {
                    Format = depthFormat,
                    DepthWriteEnabled = state.DepthTest && state.DepthWrite,
                    DepthCompare = !state.DepthTest
                        ? CompareFunction.Always
                        : state.DepthCompare switch
                        {
                            DepthCompare.Equal => CompareFunction.Equal,
                            _ => CompareFunction.LessEqual,
                        },
                    DepthBias = (int)state.DepthBias.Constant,
                    DepthBiasSlopeScale = state.DepthBias.SlopeScale,
                    DepthBiasClamp = 0.0f,
                    StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
                    StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
                };

                pDepthStencil = &depthStencil;
            }

            FragmentState fragment = new()
            {
                Module = module,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &target,
            };

            RenderPipelineDescriptor descriptor = new()
            {
                Label = labelPtr,
                Layout = layout,
                Vertex = new VertexState
                {
                    Module = module,
                    EntryPoint = vertexEntry,
                    BufferCount = bufferCount,
                    Buffers = buffers,
                },
                Primitive = new PrimitiveState
                {
                    Topology = topology,
                    StripIndexFormat = IndexFormat.Undefined,
                    FrontFace = FrontFace.Ccw,
                    CullMode = cullMode,
                },
                DepthStencil = pDepthStencil,
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment,
            };

            return api.DeviceCreateRenderPipeline(device, in descriptor);
        }
        finally
        {
            SilkMarshal.Free((nint)vertexEntry);
            SilkMarshal.Free((nint)fragmentEntry);
            if (labelPtr is not null) SilkMarshal.Free((nint)labelPtr);
        }
    }

    private static void CreateUniforms(
        Silk.NET.WebGPU.WebGPU api, Device* device, BindGroupLayout* layout, uint size,
        out WgpuBuffer* buffer, out BindGroup* bindGroup)
    {
        BufferDescriptor bufferDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = Math.Max(size, 64u),
        };

        WgpuBuffer* b = api.DeviceCreateBuffer(device, in bufferDescriptor);

        BindGroupEntry entry = new()
        {
            Binding = 0,
            Buffer = b,
            Offset = 0,
            Size = bufferDescriptor.Size,
        };

        byte* label = (byte*)SilkMarshal.StringToPtr("Pipeline.StaticUniform");
        BindGroupDescriptor descriptor = new()
        {
            Label = label,
            Layout = layout,
            EntryCount = 1,
            Entries = &entry,
        };

        buffer = b;
        bindGroup = api.DeviceCreateBindGroup(device, in descriptor);
        SilkMarshal.Free((nint)label);
    }

    /// <summary>Writes <paramref name="data"/> to the uniform buffer through the queue.</summary>
    public void UploadUniforms<T>(T data) where T : unmanaged =>
        _device.Api.QueueWriteBuffer(_device.Queue, UniformBuffer, 0, in data, (nuint)sizeof(T));

    /// <summary>Writes raw bytes to the uniform buffer.</summary>
    public void UploadUniforms(void* data, nuint size) =>
        _device.Api.QueueWriteBuffer(_device.Queue, UniformBuffer, 0, data, size);

    /// <summary>Binds this pipeline on the render pass.</summary>
    public void Bind(RenderPassEncoder* pass) =>
        _device.Api.RenderPassEncoderSetPipeline(pass, Pipeline);

    /// <summary>Binds the uniform bind group at group 0.</summary>
    /// <remarks>
    ///     Only safe for a draw that is the pass's only one, or whose uniforms every other draw in
    ///     the pass shares. Anything varying per draw has to go through
    ///     <see cref="BindNextUniforms{T}" />.
    /// </remarks>
    public void BindUniformGroup(RenderPassEncoder* pass) =>
        _device.Api.RenderPassEncoderSetBindGroup(pass, 0, UniformBindGroup, 0, null);

    /// <summary>
    ///     Writes <paramref name="data" /> to a uniform buffer no draw already recorded is reading
    ///     from, and binds it at group 0.
    /// </summary>
    /// <remarks>
    ///     A buffer per draw rather than one rewritten between them: queue writes all run before
    ///     the submitted pass does, so draws sharing a buffer would every one of them read whatever
    ///     was written last — which looks like every chunk landing on top of the final one.
    /// </remarks>
    public void BindNextUniforms<T>(RenderPassEncoder* pass, T data) where T : unmanaged
    {
        if (_uniformPoolNext == _uniformPool.Count)
        {
            _uniformPool.Add(AllocateUniforms());
        }

        (nint bufferHandle, nint groupHandle) = _uniformPool[_uniformPoolNext++];

        _device.Api.QueueWriteBuffer(
            _device.Queue, (WgpuBuffer*)bufferHandle, 0, in data, (nuint)sizeof(T));

        // A layout built with a dynamic offset on binding 0 requires SetBindGroup to supply exactly
        // one dynamic offset regardless of whether this call site is using the dynamic behaviour for
        // anything — each pooled buffer starts its own buffer, so that offset is always 0 here.
        if (_dynamicUniformLayout)
        {
            uint offset = 0;
            _device.Api.RenderPassEncoderSetBindGroup(pass, 0, (BindGroup*)groupHandle, 1, &offset);
        }
        else
        {
            _device.Api.RenderPassEncoderSetBindGroup(pass, 0, (BindGroup*)groupHandle, 0, null);
        }
    }

    /// <summary>
    ///     Hands the pool back for reuse. The caller must have submitted everything that read from
    ///     it — a buffer handed out again before then would be rewritten under a recorded draw.
    /// </summary>
    public void ResetUniformPool() => _uniformPoolNext = 0;

    /// <summary>
    ///     Writes every element of <paramref name="data" /> into one GPU buffer with a single
    ///     <c>QueueWriteBuffer</c> call, each at its own <see cref="DynamicUniformStride" />-aligned
    ///     offset, and (re)builds the batch bind group if <paramref name="data" /> no longer fits the
    ///     one from a previous call.
    /// </summary>
    /// <remarks>
    ///     Requires this pipeline's uniform binding to have been built with
    ///     <c>BufferBindingLayout.HasDynamicOffset</c> set — see <see cref="BindDynamicUniforms" />
    ///     for reading a slot back. Distinct offsets per element written once and read once each, so
    ///     this carries none of <see cref="BindNextUniforms{T}" />'s "buffers shared between draws
    ///     all read the last write" hazard.
    /// </remarks>
    public void WriteDynamicUniforms<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        int count = data.Length;
        if (count == 0) return;

        uint stride = DynamicUniformStride;
        EnsureDynamicUniformCapacity(count, stride);

        int neededBytes = count * (int)stride;
        if (_dynamicUniformStaging.Length < neededBytes)
        {
            _dynamicUniformStaging = new byte[neededBytes];
        }

        fixed (byte* dstBase = _dynamicUniformStaging)
        fixed (T* src = data)
        {
            for (int i = 0; i < count; i++)
            {
                System.Buffer.MemoryCopy(src + i, dstBase + (nint)(i * stride), stride, (uint)sizeof(T));
            }

            _device.Api.QueueWriteBuffer(_device.Queue, _dynamicUniformBuffer, 0, dstBase, (nuint)neededBytes);
        }
    }

    /// <summary>
    ///     Binds slot <paramref name="index" /> of the batch written by the last
    ///     <see cref="WriteDynamicUniforms{T}" /> call at group 0.
    /// </summary>
    public void BindDynamicUniforms(RenderPassEncoder* pass, int index)
    {
        uint offset = (uint)index * DynamicUniformStride;
        _device.Api.RenderPassEncoderSetBindGroup(pass, 0, _dynamicUniformBindGroup, 1, &offset);
    }

    private void EnsureDynamicUniformCapacity(int count, uint stride)
    {
        if (count <= _dynamicUniformCapacity && _dynamicUniformBindGroup is not null) return;

        if (_dynamicUniformBindGroup is not null) _device.Api.BindGroupRelease(_dynamicUniformBindGroup);
        if (_dynamicUniformBuffer is not null)
        {
            _device.Api.BufferDestroy(_dynamicUniformBuffer);
            _device.Api.BufferRelease(_dynamicUniformBuffer);
        }

        int newCapacity = Math.Max(count, Math.Max(_dynamicUniformCapacity * 2, 256));
        ulong bufferSize = (ulong)newCapacity * stride;

        BufferDescriptor bufferDescriptor = new()
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = bufferSize,
        };

        _dynamicUniformBuffer = _device.Api.DeviceCreateBuffer(_device.Device, in bufferDescriptor);

        BindGroupEntry entry = new()
        {
            Binding = 0,
            Buffer = _dynamicUniformBuffer,
            Offset = 0,
            Size = _uniformSize,
        };

        BindGroupDescriptor descriptor = new()
        {
            Layout = BindGroupLayout,
            EntryCount = 1,
            Entries = &entry,
        };

        _dynamicUniformBindGroup = _device.Api.DeviceCreateBindGroup(_device.Device, in descriptor);
        _dynamicUniformCapacity = newCapacity;
    }

    private (nint Buffer, nint Group) AllocateUniforms()
    {
        CreateUniforms(_device.Api, _device.Device, BindGroupLayout, _uniformSize,
            out WgpuBuffer* buffer, out BindGroup* group);

        return ((nint)buffer, (nint)group);
    }

    /// <summary>Binds an external bind group (textures, etc.) at <paramref name="groupIndex"/>.</summary>
    public static void BindGroup(RenderPassEncoder* pass, uint groupIndex, BindGroup* group, Silk.NET.WebGPU.WebGPU api) =>
        api.RenderPassEncoderSetBindGroup(pass, groupIndex, group, 0, null);

    private static BlendComponent BlendFor(BlendMode mode) => mode switch
    {
        BlendMode.None => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.One,
            DstFactor = BlendFactor.Zero,
        },
        BlendMode.Alpha => Alpha(),
        BlendMode.Additive => Add(),
        BlendMode.AdditiveByAlpha => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.SrcAlpha,
            DstFactor = BlendFactor.One,
        },
        BlendMode.SourceToDestinationAlpha => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.SrcAlpha,
            DstFactor = BlendFactor.DstAlpha,
        },
        BlendMode.Multiply => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.Dst,
            DstFactor = BlendFactor.Src,
        },
        BlendMode.Invert => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.OneMinusDst,
            DstFactor = BlendFactor.OneMinusSrc,
        },
        BlendMode.Darken => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.Zero,
            DstFactor = BlendFactor.OneMinusSrc,
        },
        _ => Alpha(),
    };

    private static BlendComponent AlphaBlendFor(BlendMode mode) => mode switch
    {
        BlendMode.None => new BlendComponent
        {
            Operation = BlendOperation.Add,
            SrcFactor = BlendFactor.One,
            DstFactor = BlendFactor.Zero,
        },
        BlendMode.Alpha => Alpha(),
        BlendMode.Additive => Add(),
        BlendMode.AdditiveByAlpha => Add(),
        BlendMode.SourceToDestinationAlpha => Alpha(),
        BlendMode.Multiply => Add(),
        BlendMode.Invert => Alpha(),
        BlendMode.Darken => Alpha(),
        _ => Alpha(),
    };

    private static BlendComponent Alpha() => new()
    {
        Operation = BlendOperation.Add,
        SrcFactor = BlendFactor.SrcAlpha,
        DstFactor = BlendFactor.OneMinusSrcAlpha,
    };

    private static BlendComponent Add() => new()
    {
        Operation = BlendOperation.Add,
        SrcFactor = BlendFactor.One,
        DstFactor = BlendFactor.One,
    };

    private static bool BlendIsIdentity(BlendMode mode) => mode == BlendMode.None;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;

        foreach ((nint buffer, nint group) in _uniformPool)
        {
            api.BindGroupRelease((BindGroup*)group);
            api.BufferDestroy((WgpuBuffer*)buffer);
            api.BufferRelease((WgpuBuffer*)buffer);
        }

        _uniformPool.Clear();

        if (_dynamicUniformBindGroup is not null) api.BindGroupRelease(_dynamicUniformBindGroup);
        if (_dynamicUniformBuffer is not null)
        {
            api.BufferDestroy(_dynamicUniformBuffer);
            api.BufferRelease(_dynamicUniformBuffer);
        }

        if (UniformBindGroup is not null) api.BindGroupRelease(UniformBindGroup);
        if (UniformBuffer is not null) api.BufferDestroy(UniformBuffer);
        if (UniformBuffer is not null) api.BufferRelease(UniformBuffer);
        if (Pipeline is not null) api.RenderPipelineRelease(Pipeline);
        if (Layout is not null) api.PipelineLayoutRelease(Layout);
        if (TextureArrayBindGroupLayout is not null) api.BindGroupLayoutRelease(TextureArrayBindGroupLayout);
        if (TextureBindGroupLayout is not null) api.BindGroupLayoutRelease(TextureBindGroupLayout);
        if (BindGroupLayout is not null) api.BindGroupLayoutRelease(BindGroupLayout);
        if (Module is not null) api.ShaderModuleRelease(Module);
    }
}
