using System.Runtime.InteropServices;
using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.Textures;
using Silk.NET.Maths;
using Shader = BetaSharp.Client.Rendering.Core.Shader;

namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
/// Batches posed/lit entity model geometry (baked by <see cref="Models.ModelPart"/>) into one streaming VBO, flushing to a dedicated GLSL shader.
/// </summary>
public sealed unsafe class EntityBatchRenderer : IDisposable
{
    private static EntityBatchRenderer? s_instance;
    public static EntityBatchRenderer Instance =>
        s_instance ?? throw new InvalidOperationException($"{nameof(EntityBatchRenderer)}.{nameof(Initialize)} must be called before use.");

    public static void Initialize(GameOptions options) => s_instance ??= new EntityBatchRenderer(options);

    private const int MaxVertices = 65536;

    private readonly Shader? _shader;
    private readonly IGL? _glOrNull;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly EntityVertex[] _vertices = new EntityVertex[MaxVertices];
    private readonly Dictionary<uint, int> _glTexToLogicalId = [];

    /// <summary>Where the queued vertices are rewritten for the seam. Null under OpenGL.</summary>
    private readonly Vertex[]? _seamVertices;

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private bool _active;

    private EntityBatchRenderer(GameOptions options)
    {
        // The shader is GLSL and the VAO is a GL object, so on a backend that has neither the batch
        // owns no resources of its own and goes out through the draw-command seam instead.
        if (GLManager.GLOrNull is not { } gl)
        {
            _seamVertices = new Vertex[MaxVertices];
            GLManager.RasterStateChanging += Flush;
            return;
        }

        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("entity_batch"),
            "shaders/entity_batch.vert",
            "shaders/entity_batch.frag");
        _glOrNull = gl;

        _vaoId = gl.GenVertexArray();
        _vboId = gl.GenBuffer();

        gl.BindVertexArray(_vaoId);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);
        gl.BufferData(GLEnum.ArrayBuffer, (nuint)(MaxVertices * sizeof(EntityVertex)), null, GLEnum.StreamDraw);

        const uint stride = 28;

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);

        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (void*)12);

        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, stride, (void*)20);

        // Integer attribute: the I-variant keeps the part id an exact uint instead of converting it.
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribIPointer(3, 1, GLEnum.UnsignedInt, stride, (void*)24);

        gl.BindVertexArray(0);
        gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        // Queued geometry must be drawn under the blend, depth and alpha state it was posed with,
        // and renderers flip that state freely between parts of the same mob.
        GLManager.RasterStateChanging += Flush;
    }

    private IGL Gl => _glOrNull ?? throw NotOnThisBackend();

    private Shader Shader => _shader ?? throw NotOnThisBackend();

    private static InvalidOperationException NotOnThisBackend() =>
        new($"{nameof(EntityBatchRenderer)}'s GL batch was reached on a backend that has no GL. "
            + "The seam path should have taken this draw.");

    /// <summary>What the caller has bound, in whichever name space this backend hands out.</summary>
    private uint BoundTextureId => _glOrNull?.BoundTexture2D ?? Texture2D.Bound?.Id ?? 0;

    /// <summary>
    /// Opens a batching pass. Only affects how long geometry may sit queued; submissions made
    /// outside a pass still draw, one part at a time.
    /// </summary>
    public void Begin()
    {
        _active = true;
        _vertexCount = 0;
        _currentTextureId = 0;
        _useTexture = true;
    }

    public void End()
    {
        Flush();
        _active = false;
    }

    /// <summary>
    /// Associates a GL texture with the symbolic id its asset path is mapped to, so a flush can
    /// tell the shader which entity it is drawing. Unregistered textures resolve to 0.
    /// </summary>
    public void RegisterTextureByPath(string assetPath, uint glTexId)
    {
        int logicalId = EntityShaderIds.ForTexture(assetPath);
        if (logicalId != 0)
        {
            _glTexToLogicalId[glTexId] = logicalId;
        }
    }

    public void SetTexture(uint texId)
    {
        if (_useTexture && texId == _currentTextureId) return;
        Flush();
        _currentTextureId = texId;
        _useTexture = true;
    }

    /// <summary>Used for solid-color passes (hurt flash, damage overlay) that render posed model geometry without sampling a texture.</summary>
    public void SetNoTexture()
    {
        if (!_useTexture) return;
        Flush();
        _useTexture = false;
    }

    public void SubmitTriangles(ReadOnlySpan<EntityVertex> verts)
    {
        if (verts.Length > MaxVertices)
        {
            throw new ArgumentException(
                $"A single submission of {verts.Length} vertices exceeds the {MaxVertices}-vertex batch.", nameof(verts));
        }

        if (_vertexCount + verts.Length > MaxVertices)
        {
            Flush();
        }

        verts.CopyTo(_vertices.AsSpan(_vertexCount));
        _vertexCount += verts.Length;

        if (_active) return;

        // No pass is open — the first-person hand, the inventory mob preview. Nothing downstream
        // will flush, so draw it now, against whatever texture the caller has bound; those paths
        // bind directly rather than going through EntityRenderer.loadTexture.
        _currentTextureId = BoundTextureId;
        _useTexture = true;
        Flush();
    }

    /// <summary>
    /// Draws everything queued so far. The batch owns a texture, a VAO and a buffer that the
    /// surrounding immediate-mode code knows nothing about, so all three are put back on the way
    /// out: a flush is free to happen between any two draws and must leave no trace.
    /// <para>
    /// Uniforms are uploaded here rather than once per pass. Projection, fog and the alpha
    /// threshold all change while a pass is open — the damage overlay renders with alpha testing
    /// off — and a flush is the last moment the queued geometry is still the state's contemporary.
    /// </para>
    /// </summary>
    public void Flush()
    {
        if (_vertexCount == 0) return;

        if (_seamVertices is not null)
        {
            FlushThroughSeam(_seamVertices);
            return;
        }

        uint callerTexture = Gl.BoundTexture2D;

        Gl.UseProgram(Shader.ProgramId);
        UploadState();

        Gl.ActiveTexture(GLEnum.Texture0);
        Gl.BindTexture(GLEnum.Texture2D, _currentTextureId);

        Gl.BindVertexArray(_vaoId);
        Gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);

        Gl.BufferSubData(GLEnum.ArrayBuffer, 0, new ReadOnlySpan<EntityVertex>(_vertices, 0, _vertexCount));

        Gl.DrawArrays(GLEnum.Triangles, 0, (uint)_vertexCount);

        Gl.BindVertexArray(0);
        // Array-buffer binding is not VAO state, so unbinding the VAO does not release it.
        Gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _vertexCount = 0;

        Gl.UseProgram(0);
        Gl.BindTexture(GLEnum.Texture2D, callerTexture);
    }

    /// <summary>
    ///     Draws the queued geometry as a <see cref="DrawCommand" />, for a backend with no GLSL
    ///     program to bind.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The model-view is identity for the length of the draw because
    ///         <see cref="Models.ModelPart" /> has already baked it into the positions, along with
    ///         the lighting and the tint. Leaving the caller's in place would apply it twice.
    ///     </para>
    ///     <para>
    ///         What the seam's vertex cannot carry is the part id, so the per-part effects a pack
    ///         can drive through <see cref="EntityShaderIds" /> do not reach this path.
    ///     </para>
    /// </remarks>
    private void FlushThroughSeam(Vertex[] scratch)
    {
        // The queued geometry belongs to the texture that was bound when it was posed, which is not
        // necessarily the one bound now: a texture change flushes before it takes effect.
        Texture2D? caller = Texture2D.Bound;
        Texture2D? batch = _useTexture ? Texture2D.Find(_currentTextureId) : null;
        batch?.Bind();

        Span<Vertex> converted = scratch.AsSpan(0, _vertexCount);
        for (int i = 0; i < _vertexCount; i++)
        {
            ref readonly EntityVertex source = ref _vertices[i];
            converted[i] = new Vertex(source.X, source.Y, source.Z, source.U, source.V, (int)source.Color, 0);
        }

        GLManager.ModelView.Push();
        GLManager.ModelView.LoadIdentity();

        try
        {
            GLManager.DrawTarget.Submit(new DrawCommand
            {
                Vertices = MemoryMarshal.AsBytes(converted),
                VertexCount = _vertexCount,
                Topology = DrawTopology.Triangles,
                Channels = batch is null
                    ? VertexChannels.Color
                    : VertexChannels.Color | VertexChannels.Texture,
                Slot = ProgramSlot.Textured,
            });
        }
        finally
        {
            GLManager.ModelView.Pop();
            _vertexCount = 0;
            caller?.Bind();
        }
    }

    /// <summary>Mirrors the fixed-function state the queued vertices were posed under.</summary>
    private void UploadState()
    {
        Matrix4X4<float> projection = GLManager.Projection.Top;

        FogState fog = GLManager.Fog;

        Shader.SetUniformMatrix4("projectionMatrix", projection);
        Shader.SetUniform1("textureSampler", 0);
        Shader.SetUniform1("useTexture", _useTexture ? 1 : 0);
        Shader.SetUniform1("entityId",
            _useTexture ? _glTexToLogicalId.GetValueOrDefault(_currentTextureId) : 0);
        Shader.SetUniform1("alphaThreshold", GLManager.EffectiveAlphaThreshold);
        Shader.SetUniform1("fogEnabled", GLManager.FogEnabled ? 1 : 0);
        Shader.SetUniform1("fogMode", (int)fog.Curve);
        Shader.SetUniform1("fogStart", fog.Start);
        Shader.SetUniform1("fogEnd", fog.End);
        Shader.SetUniform1("fogDensity", fog.Density);
        Shader.SetUniform4("fogColor", fog.Color);
    }

    public void Dispose()
    {
        GLManager.RasterStateChanging -= Flush;
        _glOrNull?.DeleteBuffer(_vboId);
        _glOrNull?.DeleteVertexArray(_vaoId);
        _shader?.Dispose();
    }
}
