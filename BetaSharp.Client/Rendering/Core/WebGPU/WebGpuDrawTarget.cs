using System.Numerics;
using System.Runtime.InteropServices;
using BetaSharp.Client.Rendering.Core.Textures;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     Draws <see cref="DrawCommand" />s with WebGPU, inside a render pass the renderer opens.
/// </summary>
/// <remarks>
///     <para>
///         The pass is the reason this exists at all rather than being a set of calls behind
///         <see cref="IGL" />: WebGPU has nowhere to record a draw outside one, so a target is only
///         able to answer between <see cref="BeginPass" /> and <see cref="EndPass" />, and a draw
///         that arrives outside that window is a bug in the caller rather than something to swallow.
///     </para>
///     <para>
///         Everything a GL draw would have inherited from global state — the matrices, the tint,
///         the alpha test, the blend and depth rules, the bound texture — is read here at
///         submission and baked into a pipeline and a uniform buffer. That is sound only because
///         these draws are immediate: the vertices are handed over and recorded before anything
///         moves the state again.
///     </para>
/// </remarks>
public sealed unsafe class WebGpuDrawTarget : IDrawTarget, IDisposable
{
    /// <summary>Bytes the streaming vertex buffer holds; one is taken per submission.</summary>
    private const ulong StreamBufferBytes = 1 << 20;

    private readonly WebGpuDevice _device;
    private readonly TextureFormat _colorFormat;
    private readonly TextureFormat _depthFormat;

    private readonly Dictionary<(bool Textured, DrawTopology Topology, RenderState State), Program> _programs = [];
    private readonly List<WgpuDynamicBuffer> _streams = [];

    private RenderPassEncoder* _pass;
    private int _streamIndex;
    private bool _disposed;

    public WebGpuDrawTarget(WebGpuDevice device, TextureFormat colorFormat, TextureFormat depthFormat)
    {
        _device = device;
        _colorFormat = colorFormat;
        _depthFormat = depthFormat;
    }

    /// <summary>Opens the window in which draws are accepted, on a pass the renderer has begun.</summary>
    public void BeginPass(RenderPassEncoder* pass)
    {
        _pass = pass;
        _streamIndex = 0;

        foreach (Program program in _programs.Values)
        {
            program.ResetUniforms();
        }
    }

    /// <summary>Closes that window. The pass itself is the caller's to end.</summary>
    public void EndPass() => _pass = null;

    public void Submit(in DrawCommand command)
    {
        if (command.VertexCount == 0)
        {
            return;
        }

        RequirePass();

        WgpuDynamicBuffer stream = NextStream();
        stream.Write(command.Vertices);

        Record(stream.Buffer, command.VertexCount, command.Topology, command.Channels);
    }

    /// <summary>
    ///     Uploads the vertices to a buffer of their own, which the returned mesh draws from.
    /// </summary>
    /// <remarks>
    ///     Needs no render pass, unlike <see cref="Submit" />: creating a buffer is a device
    ///     operation, and the sky, stars and clouds are all built during startup.
    /// </remarks>
    public IStaticMesh Capture(in DrawCommand command) =>
        new StaticMesh(this, new WgpuMesh(_device, command.Vertices, TessellatorVertexLayout.Stride),
            command.VertexCount, command.Topology, command.Channels);

    /// <summary>Records one draw of <paramref name="vertices" /> under the state now in effect.</summary>
    private void Record(WgpuBuffer* vertices, int vertexCount, DrawTopology topology, VertexChannels channels)
    {
        if (topology == DrawTopology.TriangleFan)
        {
            throw new NotSupportedException(
                "WebGPU has no triangle-fan topology. The batch has to be built as a triangle list.");
        }

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        bool textured = channels.HasFlag(VertexChannels.Texture);

        Program program = ProgramFor(textured, topology);
        api.RenderPassEncoderSetPipeline(_pass, program.Pipeline.Pipeline);

        program.NextUniforms(out WgpuBuffer* uniformBuffer, out BindGroup* uniformGroup);
        WriteUniforms(uniformBuffer, channels);
        api.RenderPassEncoderSetBindGroup(_pass, 0, uniformGroup, 0, null);

        if (textured)
        {
            BindGroup* texture = TextureBindGroup(program);
            if (texture is null)
            {
                // Nothing to sample. Drawing anyway would put an untextured quad on screen, which
                // is harder to trace back than a missing one.
                return;
            }

            api.RenderPassEncoderSetBindGroup(_pass, 1, texture, 0, null);
        }

        api.RenderPassEncoderSetVertexBuffer(_pass, 0, vertices, 0, WholeBuffer);
        api.RenderPassEncoderDraw(_pass, (uint)vertexCount, 1, 0, 0);
    }

    private void RequirePass()
    {
        if (_pass is null)
        {
            throw new InvalidOperationException(
                "Geometry was submitted to the WebGPU target outside a render pass. WebGPU cannot record a draw outside one; the caller has to draw within the renderer's pass.");
        }
    }

    /// <summary>What wgpu-native reads as "the rest of the buffer".</summary>
    private const ulong WholeBuffer = ulong.MaxValue;

    /// <summary>The bind group for whatever texture the caller last bound, materializing it if needed.</summary>
    private BindGroup* TextureBindGroup(Program program)
    {
        if (Texture2D.Bound?.Wgpu is not { } texture)
        {
            return null;
        }

        BindGroupLayout* layout = program.Pipeline.TextureBindGroupLayout;
        return layout is null ? null : texture.BindGroupFor(layout);
    }

    private void WriteUniforms(WgpuBuffer* buffer, VertexChannels channels)
    {
        RenderContext context = GLManager.Context;
        Vector4D<float> tint = context.Color;

        GbuffersUniforms uniforms = new()
        {
            ModelViewMatrix = ToNumerics(context.ModelView.Top),
            ProjectionMatrix = ToNumerics(context.Projection.Top),
            Tint = new Vector4(tint.X, tint.Y, tint.Z, tint.W),
            UseVertexColor = channels.HasFlag(VertexChannels.Color) ? 1.0f : 0.0f,
            AlphaThreshold = context.AlphaTestEnabled ? context.AlphaThreshold : 0.0f,
        };

        _device.Api.QueueWriteBuffer(_device.Queue, buffer, 0, &uniforms, GbuffersUniforms.Size);
    }

    private static Matrix4x4 ToNumerics(Matrix4X4<float> m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);

    /// <summary>
    ///     A buffer no draw already recorded in this pass is reading from.
    /// </summary>
    /// <remarks>
    ///     One per submission rather than a ring of ten as the GL target uses, because the writes
    ///     are queued and every draw in the pass executes after all of them: two draws sharing a
    ///     buffer would both read whatever was written last.
    /// </remarks>
    private WgpuDynamicBuffer NextStream()
    {
        if (_streamIndex == _streams.Count)
        {
            _streams.Add(new WgpuDynamicBuffer(_device, StreamBufferBytes));
        }

        return _streams[_streamIndex++];
    }

    private Program ProgramFor(bool textured, DrawTopology topology)
    {
        RenderState state = GLManager.State.Current;

        if (_programs.TryGetValue((textured, topology, state), out Program? cached))
        {
            return cached;
        }

        Program program = new(BuildPipeline(textured, topology, state), _device);
        _programs[(textured, topology, state)] = program;
        return program;
    }

    private WgpuPipeline BuildPipeline(bool textured, DrawTopology topology, RenderState state)
    {
        string source = AssetManager.Instance
            .getAsset(textured ? "shaders/gbuffers_textured.wgsl" : "shaders/gbuffers_basic.wgsl")
            .GetTextContent();

        // Only the attributes the shader declares. The Tessellator vertex also carries a normal, an
        // array layer and two light channels; nothing in this pair of programs reads them yet.
        VertexAttribute* attributes = stackalloc VertexAttribute[3];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Unorm8x4, Offset = 20, ShaderLocation = 1 };
        attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 12, ShaderLocation = 2 };

        VertexBufferLayout layout = new()
        {
            ArrayStride = TessellatorVertexLayout.Stride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = textured ? 3u : 2u,
            Attributes = attributes,
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
                    MinBindingSize = GbuffersUniforms.Size,
                },
            },
        ];

        BindGroupLayoutEntry[] textureEntries = textured
            ?
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
            ]
            : [];

        return new WgpuPipeline(
            _device, source, "vs_main",
            GbuffersUniforms.Size,
            uniformEntries,
            textureEntries,
            &layout, 1,
            state,
            _colorFormat,
            _depthFormat,
            ToPrimitiveTopology(topology));
    }

    private static PrimitiveTopology ToPrimitiveTopology(DrawTopology topology) => topology switch
    {
        DrawTopology.Points => PrimitiveTopology.PointList,
        DrawTopology.Lines => PrimitiveTopology.LineList,
        DrawTopology.LineStrip => PrimitiveTopology.LineStrip,
        DrawTopology.Triangles => PrimitiveTopology.TriangleList,
        DrawTopology.TriangleStrip => PrimitiveTopology.TriangleStrip,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unhandled topology."),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (Program program in _programs.Values) program.Dispose();
        _programs.Clear();

        foreach (WgpuDynamicBuffer stream in _streams) stream.Dispose();
        _streams.Clear();
    }

    /// <inheritdoc cref="IStaticMesh" />
    private sealed class StaticMesh(
        WebGpuDrawTarget target,
        WgpuMesh mesh,
        int vertexCount,
        DrawTopology topology,
        VertexChannels channels) : IStaticMesh
    {
        public void DrawWithBoundProgram() => Draw();

        public void Draw(ProgramSlot slot) => Draw();

        private void Draw()
        {
            if (vertexCount == 0) return;

            target.RequirePass();
            target.Record(mesh.VertexBuffer, vertexCount, topology, channels);
        }

        public void Dispose() => mesh.Dispose();
    }

    /// <summary>
    ///     A pipeline and the uniform buffers the draws made with it in one pass write into.
    /// </summary>
    /// <remarks>
    ///     A buffer per draw for the same reason a vertex buffer is: the writes all land before the
    ///     pass runs, so sharing one would give every draw in the pass the last draw's matrices.
    /// </remarks>
    private sealed class Program(WgpuPipeline pipeline, WebGpuDevice device) : IDisposable
    {
        public WgpuPipeline Pipeline { get; } = pipeline;

        private readonly List<(nint Buffer, nint Group)> _uniforms = [];
        private int _next;

        public void ResetUniforms() => _next = 0;

        public void NextUniforms(out WgpuBuffer* buffer, out BindGroup* group)
        {
            if (_next == _uniforms.Count)
            {
                _uniforms.Add(Allocate());
            }

            (nint bufferHandle, nint groupHandle) = _uniforms[_next++];
            buffer = (WgpuBuffer*)bufferHandle;
            group = (BindGroup*)groupHandle;
        }

        private (nint Buffer, nint Group) Allocate()
        {
            Silk.NET.WebGPU.WebGPU api = device.Api;

            BufferDescriptor bufferDesc = new()
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = GbuffersUniforms.Size,
            };

            WgpuBuffer* buffer = api.DeviceCreateBuffer(device.Device, in bufferDesc);

            BindGroupEntry entry = new()
            {
                Binding = 0,
                Buffer = buffer,
                Offset = 0,
                Size = GbuffersUniforms.Size,
            };

            BindGroupDescriptor groupDesc = new()
            {
                Layout = Pipeline.BindGroupLayout,
                EntryCount = 1,
                Entries = &entry,
            };

            BindGroup* group = api.DeviceCreateBindGroup(device.Device, in groupDesc);
            return ((nint)buffer, (nint)group);
        }

        public void Dispose()
        {
            Silk.NET.WebGPU.WebGPU api = device.Api;

            foreach ((nint buffer, nint group) in _uniforms)
            {
                api.BindGroupRelease((BindGroup*)group);
                api.BufferDestroy((WgpuBuffer*)buffer);
                api.BufferRelease((WgpuBuffer*)buffer);
            }

            _uniforms.Clear();
            Pipeline.Dispose();
        }
    }
}
