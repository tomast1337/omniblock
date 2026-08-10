using System.Runtime.InteropServices;
using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Core.WebGPU;
using BetaSharp.Client.Rendering.Entities.Models;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
/// Each unique <see cref="ModelPart"/>'s local geometry is uploaded once into a shared static
/// buffer. Per-frame pose (one matrix per bone) and tint go into an SSBO, consumed by
/// <c>shaders/entity_instanced.vert</c> via <c>gl_InstanceID</c>.
/// </summary>
public sealed unsafe class EntityInstanceBatchRenderer : IDisposable
{
    private static EntityInstanceBatchRenderer? s_instance;
    public static EntityInstanceBatchRenderer Instance =>
        s_instance ?? throw new InvalidOperationException($"{nameof(EntityInstanceBatchRenderer)}.{nameof(Initialize)} must be called before use.");

    public static void Initialize(GameOptions options) => s_instance ??= new EntityInstanceBatchRenderer(options);

    /// <summary>Upper bound on distinct entities drawn in one frame's worth of instanced batches.</summary>
    private const int MaxInstances = 1024;

    /// <summary>Floats per <c>EntityInstance</c> struct: MaxPartsPerModel 4x4 matrices, plus a vec4 tint.</summary>
    private const int FloatsPerInstance = ModelPart.MaxPartsPerModel * 16 + 4;

    // WebGPU path. Keyed by RenderState because a WgpuPipeline bakes blend/depth/cull into an
    // immutable descriptor — unlike GL, which reads it back off GLManager.State per bucket. Each
    // RenderState's own storage buffer, not one shared one, because its bind group is locked to
    // the BindGroupLayout its owning pipeline built; a second pipeline's layout object is not
    // interchangeable with it even when the entries are identical (see WgpuParticleRenderer's
    // per-layer buffers for the same shape of trap, there for a different reason).
    private WgpuMesh? _staticMesh;
    private readonly Dictionary<RenderState, WgpuPipeline> _wgpuPipelines = [];

    // A pool per RenderState, not one buffer rewritten per Flush: GLManager.ImmediateGeometryDrawing
    // flushes queued instances before every interleaved immediate-mode draw (a burning entity's
    // flame quad is the case that shows it), so one frame can call Flush more than once. All
    // QueueWriteBuffer calls run before the frame's draws execute, so a second flush's write would
    // overwrite the first flush's data out from under its still-pending draw — the same trap
    // WgpuPipeline's per-draw uniform pool exists to avoid.
    private readonly Dictionary<RenderState, List<WgpuStorageBuffer>> _wgpuStorageBufferPools = [];
    private readonly Dictionary<RenderState, int> _wgpuStorageBufferPoolNext = [];

    /// <summary>Stands in for group 2 on a colour-only draw, whose pipeline still declares it.</summary>
    private WgpuTexture? _emptyTexture2D;

    // Grows as ModelParts bake, keyed by ModelPart.StaticVertexOffset. Staged on the CPU and
    // uploaded to a WgpuMesh once, lazily, in EnsureStaticMeshUploaded.
    private readonly List<EntityInstancedVertex> _staticVertices = [];

    // Layout mirrors the SSBO's std430 EntityInstance array: MaxPartsPerModel*16 floats of pose
    // matrices then 4 floats of tint, per instance. Written in submission order, which interleaves
    // different models (entities aren't grouped by type) — _bucketInstanceIndices below tracks
    // which of these slots belong to which bucket, and Flush reorders into _flushData so each
    // bucket's instances are actually contiguous before the draw.
    private readonly float[] _instanceData = new float[MaxInstances * FloatsPerInstance];
    private readonly float[] _flushData = new float[MaxInstances * FloatsPerInstance];
    private int _instanceCount;

    /// <summary>
    ///     Everything about how a draw is rasterised and shaded that a caller can change between one
    ///     submission and the next.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An instance is not drawn where it is submitted but when the batch is flushed, so all
    ///         of this has to be recorded at submission. It used to be read from the GL state at
    ///         flush instead, which meant every instance was drawn under the state of whatever
    ///         happened to run last — the reason four renderers had to opt out of instancing
    ///         entirely to get a translucent slime shell, an additive creeper glow, a sign board and
    ///         a depth-equal hurt flash.
    ///     </para>
    ///     <para>
    ///         The projection matrix and the fog are deliberately not here. Both are set once per
    ///         render pass and nothing inside the pass changes them, so sampling them at flush is
    ///         still correct. If that ever stops being true they belong here too, and the symptom
    ///         will look exactly like the four bugs above.
    ///     </para>
    /// </remarks>
    private readonly record struct DrawState(
        RenderState Raster,
        bool UseTexture,
        float AlphaThreshold,
        bool LightingEnabled,
        LightingState Lighting,
        Matrix4X4<float> TextureMatrix);

    // One bucket per (model, texture, draw state) seen this frame.
    private readonly record struct Bucket(int VertexBase, int VertexCount, uint TextureId, DrawState Draw);
    private readonly List<Bucket> _buckets = [];
    private readonly List<List<int>> _bucketInstanceIndices = [];
    private readonly Dictionary<(ModelBase Model, uint TextureId, DrawState Draw), int> _bucketIndexByKey = [];

    private bool _active;
    private bool _flushing;

    private EntityInstanceBatchRenderer(GameOptions options)
    {
        // An instance is drawn when the batch is flushed, not where it was submitted, so anything
        // drawn immediately in between would reach the depth buffer first and reject the geometry
        // that was logically in front of it. A burning entity is the case that shows it: its flames
        // are a camera-facing quad drawn straight through the tessellator, so which parts of the
        // still-queued mob they cover changes as the camera moves.
        GLManager.ImmediateGeometryDrawing += Flush;
    }

    /// <summary>
    /// Stages one <see cref="ModelPart"/>'s baked local geometry at its assigned
    /// <see cref="ModelPart.StaticVertexOffset"/>. Safe to call before the GPU buffer exists;
    /// <see cref="EnsureStaticMeshUploaded"/> uploads lazily. No-op if already staged.
    /// </summary>
    internal void RegisterStaticGeometry(ModelPart part, ReadOnlySpan<ModelVertexLocal> localVertices)
    {
        int offset = part.StaticVertexOffset;
        if (offset < _staticVertices.Count)
        {
            return;
        }

        if (offset != _staticVertices.Count)
        {
            throw new InvalidOperationException(
                $"Static geometry registered out of order: offset {offset} but only {_staticVertices.Count} vertices staged.");
        }

        uint localSlot = (uint)Math.Max(part.LocalSlot, 0);
        uint partId = part.SymbolicPartId;

        foreach (ModelVertexLocal v in localVertices)
        {
            _staticVertices.Add(new EntityInstancedVertex
            {
                X = v.Position.X,
                Y = v.Position.Y,
                Z = v.Position.Z,
                U = v.U,
                V = v.V,
                NX = v.Normal.X,
                NY = v.Normal.Y,
                NZ = v.Normal.Z,
                LocalSlot = localSlot,
                PartId = partId,
            });
        }
    }

    /// <summary>Whether a pass is open. Gates <see cref="Models.BbModelEntityModel.Render"/>'s choice of path.</summary>
    public bool IsActive => _active;

    /// <summary>Opens a per-frame instancing pass.</summary>
    public void Begin()
    {
        _active = true;
        ResetBuckets();
        foreach (WgpuPipeline pipeline in _wgpuPipelines.Values)
        {
            pipeline.ResetUniformPool();
        }

        List<RenderState> states = [.. _wgpuStorageBufferPoolNext.Keys];
        foreach (RenderState state in states)
        {
            _wgpuStorageBufferPoolNext[state] = 0;
        }
    }

    // Clears the inner per-bucket List<int>s in place rather than dropping them, so their
    // backing arrays get reused frame to frame instead of reallocated.
    private void ResetBuckets()
    {
        _instanceCount = 0;
        _buckets.Clear();
        foreach (List<int> indices in _bucketInstanceIndices) indices.Clear();
        _bucketIndexByKey.Clear();
    }

    /// <summary>
    /// Queues one entity's pose. <paramref name="poseMatrices"/> is indexed by
    /// <see cref="ModelPart.LocalSlot"/>; scale must already be folded in (see
    /// <see cref="Models.ModelPart.CapturePose"/>).
    /// </summary>
    public void SubmitInstance(BbModelEntityModel model, uint textureId, ReadOnlySpan<System.Numerics.Matrix4x4> poseMatrices, Vector4D<float> tint)
    {
        if (!_active)
        {
            throw new InvalidOperationException($"{nameof(SubmitInstance)} called outside a {nameof(Begin)}/{nameof(End)} pass.");
        }

        if (_instanceCount >= MaxInstances)
        {
            Flush();
        }

        int instanceIndex = _instanceCount++;
        int baseFloat = instanceIndex * FloatsPerInstance;

        for (int slot = 0; slot < poseMatrices.Length && slot < ModelPart.MaxPartsPerModel; ++slot)
        {
            System.Numerics.Matrix4x4 m = poseMatrices[slot];
            int o = baseFloat + slot * 16;
            _instanceData[o + 0] = m.M11; _instanceData[o + 1] = m.M12; _instanceData[o + 2] = m.M13; _instanceData[o + 3] = m.M14;
            _instanceData[o + 4] = m.M21; _instanceData[o + 5] = m.M22; _instanceData[o + 6] = m.M23; _instanceData[o + 7] = m.M24;
            _instanceData[o + 8] = m.M31; _instanceData[o + 9] = m.M32; _instanceData[o + 10] = m.M33; _instanceData[o + 11] = m.M34;
            _instanceData[o + 12] = m.M41; _instanceData[o + 13] = m.M42; _instanceData[o + 14] = m.M43; _instanceData[o + 15] = m.M44;
        }

        int tintOffset = baseFloat + ModelPart.MaxPartsPerModel * 16;
        _instanceData[tintOffset + 0] = tint.X;
        _instanceData[tintOffset + 1] = tint.Y;
        _instanceData[tintOffset + 2] = tint.Z;
        _instanceData[tintOffset + 3] = tint.W;

        DrawState draw = new(
            GLManager.State.Current,
            GLManager.TextureEnabled,
            GLManager.EffectiveAlphaThreshold,
            GLManager.LightingEnabled,
            GLManager.Lighting,
            GLManager.TextureMatrix.Top);

        // A colour-only draw samples nothing, so the bound texture is not part of what it looks
        // like. Keying on it anyway would split one bucket per texture that happened to be bound.
        uint bucketTexture = draw.UseTexture ? textureId : 0;

        (ModelBase, uint, DrawState) key = (model, bucketTexture, draw);
        if (!_bucketIndexByKey.TryGetValue(key, out int bucketIndex))
        {
            bucketIndex = _buckets.Count;
            _bucketIndexByKey[key] = bucketIndex;
            _buckets.Add(new Bucket(model.StaticVertexBase, model.StaticVertexCount, bucketTexture, draw));
            // Reuse a pooled (already-cleared) list from a previous frame if one exists at this
            // index rather than allocating a new one every frame.
            if (bucketIndex == _bucketInstanceIndices.Count)
            {
                _bucketInstanceIndices.Add([]);
            }
        }

        _bucketInstanceIndices[bucketIndex].Add(instanceIndex);
    }

    /// <summary>Draws every bucket queued since <see cref="Begin"/> and closes the pass.</summary>
    public void End()
    {
        Flush();
        _active = false;
    }

    /// <summary>
    /// Draws everything queued so far without closing the pass. Runs by itself before any
    /// immediate-mode draw, which is what keeps queued geometry in the order its caller issued it;
    /// calling it directly is only for a caller that needs the depth buffer caught up sooner than
    /// its next draw.
    /// </summary>
    public void Flush()
    {
        // Putting the pipeline state back raises RasterStateChanging, which drains the other batch,
        // and that draw would come back here with this one's buckets half torn down.
        if (_instanceCount == 0 || _flushing)
        {
            return;
        }

        _flushing = true;
        try
        {
            FlushBucketsWebGpu();
        }
        finally
        {
            _flushing = false;
        }
    }

    private unsafe void FlushBucketsWebGpu()
    {
        if (GLManager.DrawTargetOrNull is not WebGpuDrawTarget target || target.CurrentPass is null)
        {
            // No pass is open to draw into (e.g. a flush forced outside RenderEntities' pass);
            // dropping is the same failure mode the pre-instancing WebGPU path had.
            ResetBuckets();
            return;
        }

        WebGpuDevice device = WebGpuDevice.Current!;
        EnsureStaticMeshUploaded(device);
        RenderPassEncoder* pass = target.CurrentPass;

        // Grouped by RenderState because each one is a different pipeline. Processed in the order
        // each state was first seen, buckets within a state in submission order: entities whose
        // raster state actually varies mid-mob (a translucent shell, an additive glow, a
        // depth-equal flash) opt out of instancing entirely rather than reach this method (see the
        // DrawState remarks above), so this reproduces the "submission order" guarantee the GL path
        // relies on for the cases that still go through here.
        List<RenderState> statesInOrder = [];
        Dictionary<RenderState, List<int>> bucketsByState = [];
        for (int b = 0; b < _buckets.Count; b++)
        {
            RenderState state = _buckets[b].Draw.Raster;
            if (!bucketsByState.TryGetValue(state, out List<int>? list))
            {
                list = [];
                bucketsByState[state] = list;
                statesInOrder.Add(state);
            }

            list.Add(b);
        }

        // Sized to the worst case (every bucket in one state) and sliced per state below, rather
        // than stackalloc'd fresh inside the loop.
        Span<int> allBucketFirstInstance = stackalloc int[_buckets.Count];

        foreach (RenderState state in statesInOrder)
        {
            WgpuPipeline pipeline = WgpuPipelineFor(device, state);
            WgpuStorageBuffer storage = NextWgpuStorageBuffer(device, state, pipeline);

            List<int> stateBuckets = bucketsByState[state];
            Span<int> bucketFirstInstance = allBucketFirstInstance[..stateBuckets.Count];
            int cursor = 0;
            for (int i = 0; i < stateBuckets.Count; i++)
            {
                bucketFirstInstance[i] = cursor;
                foreach (int instanceIndex in _bucketInstanceIndices[stateBuckets[i]])
                {
                    Array.Copy(_instanceData, instanceIndex * FloatsPerInstance,
                        _flushData, cursor * FloatsPerInstance, FloatsPerInstance);
                    cursor++;
                }
            }

            storage.Write(new ReadOnlySpan<float>(_flushData, 0, cursor * FloatsPerInstance));
            pipeline.Bind(pass);

            for (int i = 0; i < stateBuckets.Count; i++)
            {
                Bucket bucket = _buckets[stateBuckets[i]];

                pipeline.BindNextUniforms(pass, UniformsFor(bucket.Draw));
                storage.Bind(pass, 1);

                WgpuTexture texture = (bucket.TextureId != 0 ? Texture2D.Find(bucket.TextureId)?.Wgpu : null)
                    ?? (_emptyTexture2D ??= CreateEmptyTexture2D(device));
                BindGroup* texGroup = texture.BindGroupFor(pipeline.TextureArrayBindGroupLayout!);
                WgpuPipeline.BindGroup(pass, 2, texGroup, device.Api);

                uint instanceCount = (uint)_bucketInstanceIndices[stateBuckets[i]].Count;
                _staticMesh!.DrawRange(pass, (uint)bucket.VertexBase, (uint)bucket.VertexCount,
                    instanceCount, (uint)bucketFirstInstance[i]);
            }
        }

        ResetBuckets();
    }

    private static EntityInstancedWgslUniforms UniformsFor(in DrawState draw)
    {
        FogState fog = GLManager.Fog;
        LightingState lighting = draw.Lighting;

        return new EntityInstancedWgslUniforms
        {
            ProjectionMatrix = WebGpuDrawTarget.ToNumerics(WgpuClip.FromGl(GLManager.Projection.Top)),
            TextureMatrix = WebGpuDrawTarget.ToNumerics(draw.TextureMatrix),
            Ambient = ToNumerics(lighting.Ambient),
            LightingEnabled = draw.LightingEnabled ? 1u : 0u,
            Light0Dir = ToNumerics(lighting.Light0Direction),
            UseTexture = draw.UseTexture ? 1u : 0u,
            Light0Diffuse = ToNumerics(lighting.Light0Diffuse),
            AlphaThreshold = draw.AlphaThreshold,
            Light1Dir = ToNumerics(lighting.Light1Direction),
            FogEnabled = GLManager.FogEnabled ? 1u : 0u,
            Light1Diffuse = ToNumerics(lighting.Light1Diffuse),
            FogMode = (int)fog.Curve,
            FogColor = new System.Numerics.Vector4(fog.Color.X, fog.Color.Y, fog.Color.Z, fog.Color.W),
            FogStart = fog.Start,
            FogEnd = fog.End,
            FogDensity = fog.Density,
        };
    }

    private static System.Numerics.Vector3 ToNumerics(Vector3D<float> v) => new(v.X, v.Y, v.Z);

    private unsafe WgpuPipeline WgpuPipelineFor(WebGpuDevice device, RenderState state)
    {
        if (_wgpuPipelines.TryGetValue(state, out WgpuPipeline? cached)) return cached;

        WgpuPipeline pipeline = CreateWgpuPipeline(device, state);
        _wgpuPipelines[state] = pipeline;
        return pipeline;
    }

    /// <summary>The entity_instanced.wgsl pipeline for one raster state, matching EntityInstancedVertex.</summary>
    private static unsafe WgpuPipeline CreateWgpuPipeline(WebGpuDevice device, RenderState state)
    {
        string source = AssetManager.Instance.GetAsset("shaders/entity_instanced.wgsl").GetTextContent();

        VertexAttribute* attrs = stackalloc VertexAttribute[4];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 12, ShaderLocation = 1 };
        attrs[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 20, ShaderLocation = 2 };
        attrs[3] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 32, ShaderLocation = 3 };

        VertexBufferLayout bufferLayout = new()
        {
            ArrayStride = EntityInstancedVertexStride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 4,
            Attributes = attrs,
        };

        BindGroupLayoutEntry[] uniformEntries =
        [
            new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = EntityUniformSize },
            },
        ];

        // Bound as the pipeline's "texture" group (group 1) even though it carries a storage
        // buffer — see WgpuParticleRenderer for the same repurposing.
        BindGroupLayoutEntry[] storageEntries =
        [
            new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage },
            },
        ];

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

        return new WgpuPipeline(
            device, source, "vs_main",
            EntityUniformSize,
            uniformEntries,
            storageEntries,
            &bufferLayout, 1,
            state,
            device.SurfaceFormat,
            WgpuFramebuffer.DepthFormat,
            textureArrayEntries: textureEntries);
    }

    /// <summary>Bytes of <see cref="EntityInstancedWgslUniforms" />.</summary>
    private const uint EntityUniformSize = 240;

    private unsafe WgpuStorageBuffer NextWgpuStorageBuffer(WebGpuDevice device, RenderState state, WgpuPipeline pipeline)
    {
        if (!_wgpuStorageBufferPools.TryGetValue(state, out List<WgpuStorageBuffer>? pool))
        {
            pool = [];
            _wgpuStorageBufferPools[state] = pool;
            _wgpuStorageBufferPoolNext[state] = 0;
        }

        int next = _wgpuStorageBufferPoolNext[state];
        if (next == pool.Count)
        {
            pool.Add(new WgpuStorageBuffer(device,
                (ulong)(MaxInstances * FloatsPerInstance * sizeof(float)), pipeline.TextureBindGroupLayout));
        }

        _wgpuStorageBufferPoolNext[state] = next + 1;
        return pool[next];
    }

    private static unsafe WgpuTexture CreateEmptyTexture2D(WebGpuDevice device)
    {
        WgpuTexture texture = new(device, 1, 1, 1, WgpuSamplerDescription.Nearest);
        ReadOnlySpan<byte> white = [255, 255, 255, 255];
        texture.WriteLevel(0, 0, 0, 1, 1, white);
        return texture;
    }

    /// <summary>Uploads the static model geometry to a WgpuMesh once.</summary>
    private void EnsureStaticMeshUploaded(WebGpuDevice device)
    {
        if (_staticMesh is not null) return;

        ReadOnlySpan<EntityInstancedVertex> verts = CollectionsMarshal.AsSpan(_staticVertices);
        _staticMesh = new WgpuMesh(device,
            MemoryMarshal.AsBytes(verts),
            EntityInstancedVertexStride,
            PrimitiveTopology.TriangleList);
    }

    /// <summary>Stride of <see cref="EntityInstancedVertex"/> in bytes.</summary>
    private const uint EntityInstancedVertexStride = 40;

    public void Dispose()
    {
        GLManager.ImmediateGeometryDrawing -= Flush;

        _staticMesh?.Dispose();
        foreach (List<WgpuStorageBuffer> pool in _wgpuStorageBufferPools.Values)
        {
            foreach (WgpuStorageBuffer buffer in pool) buffer.Dispose();
        }
        foreach (WgpuPipeline pipeline in _wgpuPipelines.Values) pipeline.Dispose();
        _emptyTexture2D?.Dispose();
    }
}
