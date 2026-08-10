using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     Draws particles under WebGPU as GPU-instanced billboards: one <see cref="ParticleInstance" />
///     per particle in a storage buffer, with the quad corners built in <c>particle.wgsl</c> from
///     the camera's right/up axes rather than expanded into four vertices on the CPU.
/// </summary>
/// <remarks>
///     There is no other particle renderer to keep in step with — WebGPU is the only backend, and
///     this is the sole path particles draw through.
/// </remarks>
public sealed unsafe class WgpuParticleRenderer : IDisposable
{
    /// <summary>Bytes of one <see cref="ParticleInstance" />.</summary>
    private const uint InstanceStride = 48;

    /// <summary>Bytes of the <see cref="ParticleWgslUniforms" /> block.</summary>
    private const uint UniformSize = 160;

    private WgpuPipeline? _pipeline;

    // One storage buffer per particle layer rather than one rewritten between draws: a queue write
    // always lands before the pass that reads it runs, so three draws sharing one buffer would all
    // sample whichever layer wrote last — the same trap WgpuPipeline's per-draw uniform pool exists
    // to avoid.
    private readonly WgpuStorageBuffer?[] _layerBuffers = new WgpuStorageBuffer?[3];

    /// <summary>Resets the per-draw uniform pool. Call once per frame before any layer is drawn.</summary>
    public void BeginFrame() => _pipeline?.ResetUniformPool();

    /// <summary>Draws one layer's particles as instanced billboards.</summary>
    /// <remarks>
    ///     <paramref name="modelView" /> and <paramref name="projection" /> are the same
    ///     <see cref="GLManager.ModelView" />/<see cref="GLManager.Projection" /> matrices the GL
    ///     Tessellator path submits particles under — <paramref name="projection" /> is still in
    ///     OpenGL's clip space, and only converted to WebGPU's here, at the point a shader is handed it.
    /// </remarks>
    public void DrawLayer(WebGpuDevice device, WgpuTexture texture,
        Matrix4X4<float> modelView, Matrix4X4<float> projection,
        System.Numerics.Vector3 right, System.Numerics.Vector3 up,
        int layer, ReadOnlySpan<ParticleInstance> instances)
    {
        if (instances.Length == 0) return;

        WgpuPipeline pipeline = EnsurePipeline(device);
        RenderPassEncoder* pass = ((WebGpuDrawTarget)GLManager.DrawTarget).CurrentPass;

        WgpuStorageBuffer buffer = _layerBuffers[layer] ??= new WgpuStorageBuffer(
            device, (ulong)(Particles.ParticleBuffer.MaxParticles * InstanceStride),
            pipeline.TextureBindGroupLayout);
        buffer.Write(instances);

        pipeline.Bind(pass);
        pipeline.BindNextUniforms(pass, new ParticleWgslUniforms
        {
            ModelViewMatrix = WebGpuDrawTarget.ToNumerics(modelView),
            ProjectionMatrix = WebGpuDrawTarget.ToNumerics(WgpuClip.FromGl(projection)),
            Right = right,
            Up = up,
        });
        buffer.Bind(pass, 1);

        BindGroup* textureGroup = texture.BindGroupFor(pipeline.TextureArrayBindGroupLayout!);
        WgpuPipeline.BindGroup(pass, 2, textureGroup, device.Api);

        // 6 vertices build one particle's quad (two triangles); instance_index in particle.wgsl
        // is what actually selects which particle's record each instance's corners come from.
        device.Api.RenderPassEncoderDraw(pass, 6, (uint)instances.Length, 0, 0);
    }

    private WgpuPipeline EnsurePipeline(WebGpuDevice device)
    {
        if (_pipeline is not null) return _pipeline;

        string source = AssetManager.Instance.GetAsset("shaders/particle.wgsl").GetTextContent();

        BindGroupLayoutEntry[] uniformEntries =
        [
            new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = UniformSize },
            },
        ];

        // Bound as the pipeline's "texture" group (group 1) even though it carries a storage
        // buffer, not a texture — WgpuPipeline only names the slot after its most common use.
        BindGroupLayoutEntry[] storageEntries =
        [
            new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage },
            },
        ];

        // Bound as the pipeline's "texture array" group (group 2) for the same reason — it is a
        // plain 2D texture and sampler, just occupying the slot the array-texture group leaves free
        // when a shader that wants both a texture and a storage buffer builds one.
        BindGroupLayoutEntry[] textureEntries =
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

        _pipeline = new WgpuPipeline(
            device, source, "vs_main",
            UniformSize,
            uniformEntries,
            storageEntries,
            buffers: null, bufferCount: 0,
            RenderState.Translucent,
            device.SurfaceFormat,
            WgpuFramebuffer.DepthFormat,
            PrimitiveTopology.TriangleList,
            textureArrayEntries: textureEntries);

        return _pipeline;
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        foreach (WgpuStorageBuffer? buffer in _layerBuffers)
        {
            buffer?.Dispose();
        }
    }
}
