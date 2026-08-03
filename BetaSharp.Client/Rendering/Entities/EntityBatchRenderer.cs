using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
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

    private readonly Shader _shader;
    private readonly LegacyGL _legacyGL;
    private readonly GL _silkGL;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly EntityVertex[] _vertices = new EntityVertex[MaxVertices];
    private readonly Dictionary<uint, int> _glTexToLogicalId = [];

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private bool _active;

    private EntityBatchRenderer(GameOptions options)
    {
        _shader = new Shader(
            options.ShaderOptions.GetOrCreate("entity_batch"),
            "shaders/entity_batch.vert",
            "shaders/entity_batch.frag");
        _legacyGL = (LegacyGL)GLManager.GL;
        _silkGL = _legacyGL.SilkGL;

        _vaoId = _silkGL.GenVertexArray();
        _vboId = _silkGL.GenBuffer();

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _vboId);
        _silkGL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVertices * sizeof(EntityVertex)), null, BufferUsageARB.StreamDraw);

        const uint stride = 28;

        _silkGL.EnableVertexAttribArray(0);
        _silkGL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);

        _silkGL.EnableVertexAttribArray(1);
        _silkGL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)12);

        _silkGL.EnableVertexAttribArray(2);
        _silkGL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, stride, (void*)20);

        // Integer attribute: the I-variant keeps the part id an exact uint instead of converting it.
        _silkGL.EnableVertexAttribArray(3);
        _silkGL.VertexAttribIPointer(3, 1, VertexAttribIType.UnsignedInt, stride, (void*)24);

        _silkGL.BindVertexArray(0);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        // Queued geometry must be drawn under the blend, depth and alpha state it was posed with,
        // and renderers flip that state freely between parts of the same mob.
        _legacyGL.RasterStateChanging += Flush;
    }

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
        _currentTextureId = _legacyGL.BoundTexture2D;
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

        uint callerTexture = _legacyGL.BoundTexture2D;

        GLManager.GL.UseProgram(_shader.ProgramId);
        UploadState();

        _silkGL.ActiveTexture(TextureUnit.Texture0);
        _silkGL.BindTexture(TextureTarget.Texture2D, _currentTextureId);

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _vboId);

        fixed (EntityVertex* ptr = _vertices)
        {
            _silkGL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_vertexCount * sizeof(EntityVertex)), ptr);
        }

        _silkGL.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);

        _silkGL.BindVertexArray(0);
        // Array-buffer binding is not VAO state, so unbinding the VAO does not release it.
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _vertexCount = 0;

        GLManager.GL.UseProgram(0);
        _silkGL.BindTexture(TextureTarget.Texture2D, callerTexture);
    }

    /// <summary>Mirrors the fixed-function state the queued vertices were posed under.</summary>
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

        FogState fog = GLManager.Fog;

        _shader.SetUniformMatrix4("projectionMatrix", projection);
        _shader.SetUniform1("textureSampler", 0);
        _shader.SetUniform1("useTexture", _useTexture ? 1 : 0);
        _shader.SetUniform1("entityId",
            _useTexture ? _glTexToLogicalId.GetValueOrDefault(_currentTextureId) : 0);
        _shader.SetUniform1("alphaThreshold", gl.GetCurrentAlphaThreshold());
        _shader.SetUniform1("fogEnabled", gl.GetFogEnabled() ? 1 : 0);
        _shader.SetUniform1("fogMode", (int)fog.Curve);
        _shader.SetUniform1("fogStart", fog.Start);
        _shader.SetUniform1("fogEnd", fog.End);
        _shader.SetUniform1("fogDensity", fog.Density);
        _shader.SetUniform4("fogColor", fog.Color);
    }

    public void Dispose()
    {
        _legacyGL.RasterStateChanging -= Flush;
        _silkGL.DeleteBuffer(_vboId);
        _silkGL.DeleteVertexArray(_vaoId);
        _shader.Dispose();
    }
}
