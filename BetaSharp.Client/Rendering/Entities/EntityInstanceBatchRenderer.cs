using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using BetaSharp.Client.Rendering.Entities.Models;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;
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
    private readonly IGL _gl;

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
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("entity_instanced"),
            "shaders/entity_instanced.vert",
            "shaders/entity_instanced.frag");
        _gl = GLManager.GL;

        _vaoId = _gl.GenVertexArray();
        _staticVboId = _gl.GenBuffer();
        _ssboId = _gl.GenBuffer();

        _gl.BindBuffer(GLEnum.ShaderStorageBuffer, _ssboId);
        _gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_instanceData.Length * sizeof(float)), null, GLEnum.StreamDraw);
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _ssboId);
        _gl.BindBuffer(GLEnum.ShaderStorageBuffer, 0);

        // An instance is drawn when the batch is flushed, not where it was submitted, so anything
        // drawn immediately in between would reach the depth buffer first and reject the geometry
        // that was logically in front of it. A burning entity is the case that shows it: its flames
        // are a camera-facing quad drawn straight through the tessellator, so which parts of the
        // still-queued mob they cover changes as the camera moves.
        GLManager.ImmediateGeometryDrawing += Flush;

        ConfigureVertexAttributes();
    }

    private void ConfigureVertexAttributes()
    {
        const uint stride = 40;

        _gl.BindVertexArray(_vaoId);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _staticVboId);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (void*)12);

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, GLEnum.Float, false, stride, (void*)20);

        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribIPointer(3, 1, GLEnum.UnsignedInt, stride, (void*)32);

        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribIPointer(4, 1, GLEnum.UnsignedInt, stride, (void*)36);

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
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

        _gl.BindBuffer(GLEnum.ArrayBuffer, _staticVboId);

        if (_staticVertices.Count > _staticVboCapacity)
        {
            // Reserve only. _staticVertices has fewer elements than a grown capacity would
            // request, so it can't be the BufferData source.
            _staticVboCapacity = Math.Max(_staticVertices.Count, _staticVboCapacity * 2);
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_staticVboCapacity * sizeof(EntityInstancedVertex)), null, GLEnum.StaticDraw);
            _staticVertexCountUploaded = 0;
        }

        EntityInstancedVertex[] added = [.. _staticVertices.Skip(_staticVertexCountUploaded)];
        _gl.BufferSubData(GLEnum.ArrayBuffer, _staticVertexCountUploaded * sizeof(EntityInstancedVertex),
            new ReadOnlySpan<EntityInstancedVertex>(added));

        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _staticVertexCountUploaded = _staticVertices.Count;
    }

    /// <summary>Whether a pass is open. Gates <see cref="Models.BbModelEntityModel.Render"/>'s choice of path.</summary>
    public bool IsActive => _active;

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
            FlushBuckets();
        }
        finally
        {
            _flushing = false;
        }
    }

    private void FlushBuckets()
    {
        EnsureStaticBufferUploaded();

        uint callerTexture = _gl.BoundTexture2D;
        RenderState callerState = GLManager.State.Current;

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

        _gl.BindBuffer(GLEnum.ShaderStorageBuffer, _ssboId);
        _gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, new ReadOnlySpan<float>(_flushData, 0, _instanceCount * FloatsPerInstance));

        GLManager.GL.UseProgram(_shader.ProgramId);
        UploadPassState();

        _gl.BindVertexArray(_vaoId);

        // Buckets are drawn in the order they were first submitted to, which is what keeps a
        // translucent shell behind the body it covers and a depth-equal flash behind the depths it
        // matches. Reordering them would break both.
        for (int b = 0; b < _buckets.Count; b++)
        {
            Bucket bucket = _buckets[b];
            UploadDrawState(bucket.Draw);
            _shader.SetUniform1("instanceBase", bucketStarts[b]);
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, bucket.TextureId);
            _gl.DrawArraysInstanced(GLEnum.Triangles, bucket.VertexBase, (uint)bucket.VertexCount, (uint)_bucketInstanceIndices[b].Count);
        }

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ShaderStorageBuffer, 0);

        GLManager.GL.UseProgram(0);
        _gl.BindTexture(GLEnum.Texture2D, callerTexture);

        // A flush can happen part way through a renderer, so put back the pipeline state the caller
        // was working under rather than leaving it on whichever bucket happened to be drawn last.
        GLManager.State.Apply(callerState);

        ResetBuckets();
    }

    /// <summary>
    ///     The part of the shader's state that is the same for every bucket in the flush.
    /// </summary>
    /// <remarks>
    ///     Read from GL now rather than recorded at submission, which is only correct because
    ///     nothing between a <see cref="Begin" /> and its <see cref="End" /> changes the projection
    ///     or the fog. See <see cref="DrawState" /> for the ones that could not stay here.
    /// </remarks>
    private void UploadPassState()
    {
        Matrix4X4<float> projection = GLManager.Projection.Top;

        FogState fog = GLManager.Fog;

        _shader.SetUniformMatrix4("projectionMatrix", projection);
        _shader.SetUniform1("textureSampler", 0);

        _shader.SetUniform1("fogEnabled", GLManager.FogEnabled ? 1 : 0);
        _shader.SetUniform1("fogMode", (int)fog.Curve);
        _shader.SetUniform1("fogStart", fog.Start);
        _shader.SetUniform1("fogEnd", fog.End);
        _shader.SetUniform1("fogDensity", fog.Density);
        _shader.SetUniform4("fogColor", fog.Color);
    }

    /// <summary>Puts back the state one bucket's instances were submitted under.</summary>
    private void UploadDrawState(in DrawState draw)
    {
        GLManager.State.Apply(draw.Raster);

        _shader.SetUniform1("useTexture", draw.UseTexture ? 1 : 0);
        _shader.SetUniform1("alphaThreshold", draw.AlphaThreshold);
        _shader.SetUniformMatrix4("textureMatrix", draw.TextureMatrix);

        _shader.SetUniform1("lightingEnabled", draw.LightingEnabled ? 1 : 0);
        _shader.SetUniform3("ambient", draw.Lighting.Ambient);
        _shader.SetUniform3("light0Dir", draw.Lighting.Light0Direction);
        _shader.SetUniform3("light0Diffuse", draw.Lighting.Light0Diffuse);
        _shader.SetUniform3("light1Dir", draw.Lighting.Light1Direction);
        _shader.SetUniform3("light1Diffuse", draw.Lighting.Light1Diffuse);
    }

    public void Dispose()
    {
        GLManager.ImmediateGeometryDrawing -= Flush;
        _gl.DeleteBuffer(_ssboId);
        _gl.DeleteBuffer(_staticVboId);
        _gl.DeleteVertexArray(_vaoId);
        _shader.Dispose();
    }
}
