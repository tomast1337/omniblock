using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

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
    public BindGroupLayout* BindGroupLayout { get; }
    public PipelineLayout* Layout { get; }
    public RenderPipeline* Pipeline { get; }

    /// <summary>The uniform buffer, large enough for the full <c>Uniforms</c> struct.</summary>
    public WgpuBuffer* UniformBuffer { get; }

    /// <summary>The per-frame bind group binding the uniform buffer to the layout.</summary>
    public BindGroup* UniformBindGroup { get; }

    private readonly WebGpuDevice _device;
    private bool _disposed;

    /// <summary>
    ///     Wraps an already-built pipeline. Callers that build the pipeline themselves (to match
    ///     an existing pattern exactly) hand the pieces in; callers that want the builder use the
    ///     other constructor.
    /// </summary>
    internal WgpuPipeline(
        ShaderModule* module,
        BindGroupLayout* bindGroupLayout,
        PipelineLayout* layout,
        RenderPipeline* pipeline,
        WgpuBuffer* uniformBuffer,
        BindGroup* uniformBindGroup,
        WebGpuDevice device)
    {
        _device = device;
        Module = module;
        BindGroupLayout = bindGroupLayout;
        Layout = layout;
        Pipeline = pipeline;
        UniformBuffer = uniformBuffer;
        UniformBindGroup = uniformBindGroup;
    }

    /// <summary>
    ///     Builds a shader module from WGSL source, creates the bind-group layout from
    ///     <paramref name="uniformEntries"/>, and creates a pipeline matching
    ///     <paramref name="state"/>, the vertex <paramref name="buffers"/>, and the colour format.
    /// </summary>
    /// <param name="bufferCount">How many <c>VertexBufferLayout</c> entries <paramref name="buffers"/> points to.</param>
    /// <param name="uniformSize">Byte size of the shader's <c>Uniforms</c> struct.</param>
    public WgpuPipeline(
        WebGpuDevice device,
        string wgslSource,
        string entryPoint,
        uint uniformSize,
        ReadOnlySpan<BindGroupLayoutEntry> uniformEntries,
        VertexBufferLayout* buffers,
        nuint bufferCount,
        RenderState state,
        TextureFormat colorFormat)
    {
        _device = device;
        Silk.NET.WebGPU.WebGPU api = device.Api;

        Module = CreateShaderModule(api, device.Device, wgslSource);
        BindGroupLayout = CreateBindGroupLayout(api, device.Device, uniformEntries);
        Layout = CreatePipelineLayout(api, device.Device, BindGroupLayout);
        Pipeline = CreateRenderPipeline(api, device.Device, Module, entryPoint, Layout, buffers, bufferCount, state, colorFormat);
        CreateUniforms(api, device.Device, BindGroupLayout, uniformSize, out WgpuBuffer* ub, out BindGroup* ug);
        UniformBuffer = ub;
        UniformBindGroup = ug;
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
        Silk.NET.WebGPU.WebGPU api, Device* device, BindGroupLayout* bindGroupLayout)
    {
        BindGroupLayout** layouts = &bindGroupLayout;

        PipelineLayoutDescriptor descriptor = new()
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = layouts,
        };

        return api.DeviceCreatePipelineLayout(device, in descriptor);
    }

    private static RenderPipeline* CreateRenderPipeline(
        Silk.NET.WebGPU.WebGPU api,
        Device* device,
        ShaderModule* module,
        string entryPoint,
        PipelineLayout* layout,
        VertexBufferLayout* buffers,
        nuint bufferCount,
        RenderState state,
        TextureFormat colorFormat)
    {
        byte* vertexEntry = (byte*)SilkMarshal.StringToPtr(entryPoint);
        byte* fragmentEntry = (byte*)SilkMarshal.StringToPtr("fs_main");

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

            // Depth not wired in yet — the preview has no depth attachment.
            DepthStencilState* pDepthStencil = null;

            FragmentState fragment = new()
            {
                Module = module,
                EntryPoint = fragmentEntry,
                TargetCount = 1,
                Targets = &target,
            };

            RenderPipelineDescriptor descriptor = new()
            {
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
                    Topology = PrimitiveTopology.TriangleList,
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

        BindGroupDescriptor descriptor = new()
        {
            Layout = layout,
            EntryCount = 1,
            Entries = &entry,
        };

        buffer = b;
        bindGroup = api.DeviceCreateBindGroup(device, in descriptor);
    }

    /// <summary>Writes <paramref name="data"/> to the uniform buffer through the queue.</summary>
    public void UploadUniforms<T>(T data) where T : unmanaged =>
        _device.Api.QueueWriteBuffer(_device.Queue, UniformBuffer, 0, in data, (nuint)sizeof(T));

    /// <summary>Writes raw bytes to the uniform buffer.</summary>
    public void UploadUniforms(void* data, nuint size) =>
        _device.Api.QueueWriteBuffer(_device.Queue, UniformBuffer, 0, data, size);

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
        if (UniformBindGroup is not null) api.BindGroupRelease(UniformBindGroup);
        if (UniformBuffer is not null) api.BufferDestroy(UniformBuffer);
        if (UniformBuffer is not null) api.BufferRelease(UniformBuffer);
        if (Pipeline is not null) api.RenderPipelineRelease(Pipeline);
        if (Layout is not null) api.PipelineLayoutRelease(Layout);
        if (BindGroupLayout is not null) api.BindGroupLayoutRelease(BindGroupLayout);
        if (Module is not null) api.ShaderModuleRelease(Module);
    }
}
