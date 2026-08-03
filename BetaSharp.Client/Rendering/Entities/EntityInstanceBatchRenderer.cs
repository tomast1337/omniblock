using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Entities.Models;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Shader = BetaSharp.Client.Rendering.Core.Shader;

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

    private readonly Shader _shader;
    private readonly LegacyGL _legacyGL;
    private readonly GL _silkGL;

    private readonly uint _vaoId;
    private readonly uint _staticVboId;
    private readonly uint _ssboId;

    // Grows as ModelParts bake, keyed by ModelPart.StaticVertexOffset. Staged on the CPU and
    // (re)uploaded to _staticVboId lazily, since GL may not exist yet when the first models bake.
    private readonly List<EntityInstancedVertex> _staticVertices = [];
    private int _staticVertexCountUploaded;
    private int _staticVboCapacity;

    // Layout mirrors the SSBO's std430 EntityInstance array: MaxPartsPerModel*16 floats of pose
    // matrices then 4 floats of tint, per instance. Written in submission order, which interleaves
    // different models (entities aren't grouped by type) — _bucketInstanceIndices below tracks
    // which of these slots belong to which bucket, and Flush reorders into _flushData so each
    // bucket's instances are actually contiguous before the draw.
    private readonly float[] _instanceData = new float[MaxInstances * FloatsPerInstance];
    private readonly float[] _flushData = new float[MaxInstances * FloatsPerInstance];
    private int _instanceCount;

    // One bucket per (model, texture) pair seen this frame.
    private readonly record struct Bucket(int VertexBase, int VertexCount, uint TextureId);
    private readonly List<Bucket> _buckets = [];
    private readonly List<List<int>> _bucketInstanceIndices = [];
    private readonly Dictionary<(ModelBase Model, uint TextureId), int> _bucketIndexByKey = [];

    private bool _active;

    private EntityInstanceBatchRenderer(GameOptions options)
    {
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("entity_instanced"),
            "shaders/entity_instanced.vert",
            "shaders/entity_instanced.frag");
        _legacyGL = (LegacyGL)GLManager.GL;
        _silkGL = _legacyGL.SilkGL;

        _vaoId = _silkGL.GenVertexArray();
        _staticVboId = _silkGL.GenBuffer();
        _ssboId = _silkGL.GenBuffer();

        _silkGL.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssboId);
        _silkGL.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(_instanceData.Length * sizeof(float)), null, BufferUsageARB.StreamDraw);
        _silkGL.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _ssboId);
        _silkGL.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        ConfigureVertexAttributes();
    }

    private void ConfigureVertexAttributes()
    {
        const uint stride = 40;

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _staticVboId);

        _silkGL.EnableVertexAttribArray(0);
        _silkGL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _silkGL.EnableVertexAttribArray(1);
        _silkGL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)12);

        _silkGL.EnableVertexAttribArray(2);
        _silkGL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)20);

        _silkGL.EnableVertexAttribArray(3);
        _silkGL.VertexAttribIPointer(3, 1, VertexAttribIType.UnsignedInt, stride, (void*)32);

        _silkGL.EnableVertexAttribArray(4);
        _silkGL.VertexAttribIPointer(4, 1, VertexAttribIType.UnsignedInt, stride, (void*)36);

        _silkGL.BindVertexArray(0);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    /// <summary>
    /// Stages one <see cref="ModelPart"/>'s baked local geometry at its assigned
    /// <see cref="ModelPart.StaticVertexOffset"/>. Safe to call before the GPU buffer exists;
    /// <see cref="EnsureStaticBufferUploaded"/> uploads lazily. No-op if already staged.
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

    private void EnsureStaticBufferUploaded()
    {
        if (_staticVertexCountUploaded == _staticVertices.Count)
        {
            return;
        }

        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _staticVboId);

        if (_staticVertices.Count > _staticVboCapacity)
        {
            // Reserve only. _staticVertices has fewer elements than a grown capacity would
            // request, so it can't be the BufferData source.
            _staticVboCapacity = Math.Max(_staticVertices.Count, _staticVboCapacity * 2);
            _silkGL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_staticVboCapacity * sizeof(EntityInstancedVertex)), null, BufferUsageARB.StaticDraw);
            _staticVertexCountUploaded = 0;
        }

        EntityInstancedVertex[] added = [.. _staticVertices.Skip(_staticVertexCountUploaded)];
        fixed (EntityInstancedVertex* ptr = added)
        {
            _silkGL.BufferSubData(BufferTargetARB.ArrayBuffer, _staticVertexCountUploaded * sizeof(EntityInstancedVertex), (nuint)(added.Length * sizeof(EntityInstancedVertex)), ptr);
        }

        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _staticVertexCountUploaded = _staticVertices.Count;
    }

    /// <summary>Whether a pass is open. Gates <see cref="Models.BbModelEntityModel.Render"/>'s choice of path.</summary>
    public bool IsActive => _active;

    /// <summary>
    /// Forces the legacy per-vertex path even while <see cref="IsActive"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two reasons so far. There is no no-texture draw mode here, so
    /// <see cref="LivingEntityRenderer"/>'s color-only hurt and death flash sets this rather than
    /// submitting an instance, which always samples a texture.
    /// </para>
    /// <para>
    /// And anything drawn under non-default blend, depth or alpha state has to, because an instance
    /// is not drawn where it is submitted but when the pass ends, by which time that state is gone.
    /// The legacy batch flushes on every raster state change and so keeps the state each piece of
    /// geometry was posed under; this one has no equivalent. Bucketing instances by
    /// <see cref="Core.RenderState"/> would remove the need for both of these.
    /// </para>
    /// </remarks>
    public bool ForceLegacyPath { get; set; }

    /// <summary>Opens a per-frame instancing pass.</summary>
    public void Begin()
    {
        _active = true;
        ResetBuckets();
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

        (ModelBase, uint) key = (model, textureId);
        if (!_bucketIndexByKey.TryGetValue(key, out int bucketIndex))
        {
            bucketIndex = _buckets.Count;
            _bucketIndexByKey[key] = bucketIndex;
            _buckets.Add(new Bucket(model.StaticVertexBase, model.StaticVertexCount, textureId));
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
    /// Draws everything queued so far without closing the pass. Call this before anything that
    /// needs this frame's submitted entities already in the depth buffer, e.g. before
    /// <see cref="LivingEntityRenderer"/>'s <c>DepthFunc(Equal)</c> hurt/death-flash overlay.
    /// </summary>
    public void Flush()
    {
        if (_instanceCount == 0)
        {
            return;
        }

        EnsureStaticBufferUploaded();

        uint callerTexture = _legacyGL.BoundTexture2D;

        // Submissions interleave by entity, not by bucket, so pack each bucket's instances
        // contiguously into _flushData before upload — DrawArraysInstanced needs its instances
        // at a single contiguous [instanceBase, instanceBase+count) range.
        Span<int> bucketStarts = stackalloc int[_buckets.Count];
        int cursor = 0;
        for (int b = 0; b < _buckets.Count; b++)
        {
            bucketStarts[b] = cursor;
            foreach (int instanceIndex in _bucketInstanceIndices[b])
            {
                Array.Copy(_instanceData, instanceIndex * FloatsPerInstance, _flushData, cursor * FloatsPerInstance, FloatsPerInstance);
                cursor++;
            }
        }

        _silkGL.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _ssboId);
        fixed (float* ptr = _flushData)
        {
            _silkGL.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, (nuint)(_instanceCount * FloatsPerInstance * sizeof(float)), ptr);
        }

        GLManager.GL.UseProgram(_shader.ProgramId);
        UploadState();

        _silkGL.BindVertexArray(_vaoId);

        for (int b = 0; b < _buckets.Count; b++)
        {
            Bucket bucket = _buckets[b];
            _shader.SetUniform1("instanceBase", bucketStarts[b]);
            _silkGL.ActiveTexture(TextureUnit.Texture0);
            _silkGL.BindTexture(TextureTarget.Texture2D, bucket.TextureId);
            _silkGL.DrawArraysInstanced(PrimitiveType.Triangles, bucket.VertexBase, (uint)bucket.VertexCount, (uint)_bucketInstanceIndices[b].Count);
        }

        _silkGL.BindVertexArray(0);
        _silkGL.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);

        GLManager.GL.UseProgram(0);
        _silkGL.BindTexture(TextureTarget.Texture2D, callerTexture);

        ResetBuckets();
    }

    /// <summary>Mirrors the fixed-function state the queued instances were posed under.</summary>
    private void UploadState()
    {
        EmulatedGL gl = (EmulatedGL)GLManager.GL;

        Span<float> projectionData = stackalloc float[16];
        gl.GetFloat(Core.OpenGL.GLEnum.ProjectionMatrix, projectionData);
        Matrix4X4<float> projection = new(
            projectionData[0], projectionData[1], projectionData[2], projectionData[3],
            projectionData[4], projectionData[5], projectionData[6], projectionData[7],
            projectionData[8], projectionData[9], projectionData[10], projectionData[11],
            projectionData[12], projectionData[13], projectionData[14], projectionData[15]);

        EntityFogSnapshot fog = gl.GetFogState();
        EntityLightingSnapshot lighting = gl.GetLightingState();

        _shader.SetUniformMatrix4("projectionMatrix", projection);
        _shader.SetUniform1("textureSampler", 0);
        _shader.SetUniform1("useTexture", 1);
        _shader.SetUniform1("alphaThreshold", gl.GetCurrentAlphaThreshold());

        _shader.SetUniform1("lightingEnabled", lighting.Enabled ? 1 : 0);
        _shader.SetUniform3("ambient", lighting.Ambient);
        _shader.SetUniform3("light0Dir", lighting.Light0Dir);
        _shader.SetUniform3("light0Diffuse", lighting.Light0Diffuse);
        _shader.SetUniform3("light1Dir", lighting.Light1Dir);
        _shader.SetUniform3("light1Diffuse", lighting.Light1Diffuse);

        _shader.SetUniform1("fogEnabled", fog.Enabled ? 1 : 0);
        _shader.SetUniform1("fogMode", fog.Mode);
        _shader.SetUniform1("fogStart", fog.Start);
        _shader.SetUniform1("fogEnd", fog.End);
        _shader.SetUniform1("fogDensity", fog.Density);
        _shader.SetUniform4("fogColor", fog.Color);
    }

    public void Dispose()
    {
        _silkGL.DeleteBuffer(_ssboId);
        _silkGL.DeleteBuffer(_staticVboId);
        _silkGL.DeleteVertexArray(_vaoId);
        _shader.Dispose();
    }
}
