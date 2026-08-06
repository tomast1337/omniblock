using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.WebGPU;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
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
    private readonly IGL _gl;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly EntityVertex[] _vertices = new EntityVertex[MaxVertices];
    private readonly Dictionary<uint, int> _glTexToLogicalId = [];

    // WebGPU path
    private WgpuDynamicBuffer? _gpuBuffer;

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
        _gl = GLManager.GL;

        _vaoId = _gl.GenVertexArray();
        _vboId = _gl.GenBuffer();

        _gl.BindVertexArray(_vaoId);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);
        _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(MaxVertices * sizeof(EntityVertex)), null, GLEnum.StreamDraw);

        const uint stride = 28;

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (void*)12);

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, stride, (void*)20);

        // Integer attribute: the I-variant keeps the part id an exact uint instead of converting it.
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribIPointer(3, 1, GLEnum.UnsignedInt, stride, (void*)24);

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);

        // Queued geometry must be drawn under the blend, depth and alpha state it was posed with,
        // and renderers flip that state freely between parts of the same mob.
        GLManager.RasterStateChanging += Flush;
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
        _currentTextureId = _gl.BoundTexture2D;
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

        uint callerTexture = _gl.BoundTexture2D;

        GLManager.GL.UseProgram(_shader.ProgramId);
        UploadState();

        _gl.ActiveTexture(GLEnum.Texture0);
        _gl.BindTexture(GLEnum.Texture2D, _currentTextureId);

        _gl.BindVertexArray(_vaoId);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);

        _gl.BufferSubData(GLEnum.ArrayBuffer, 0, new ReadOnlySpan<EntityVertex>(_vertices, 0, _vertexCount));

        _gl.DrawArrays(GLEnum.Triangles, 0, (uint)_vertexCount);

        _gl.BindVertexArray(0);
        // Array-buffer binding is not VAO state, so unbinding the VAO does not release it.
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _vertexCount = 0;

        GLManager.GL.UseProgram(0);
        _gl.BindTexture(GLEnum.Texture2D, callerTexture);
    }

    /// <summary>
    ///     Draws queued geometry through the native WebGPU command encoder.
    ///     The caller has already uploaded uniforms and bound the pipeline's uniform group.
    /// </summary>
    public unsafe void FlushWebGpu(RenderPassEncoder* pass, WgpuPipeline pipeline)
    {
        if (_vertexCount == 0) return;

        WebGpuDevice device = WebGpuDevice.Current!;
        _gpuBuffer ??= new WgpuDynamicBuffer(device, (ulong)(MaxVertices * sizeof(EntityVertex)));

        _gpuBuffer.Write(new ReadOnlySpan<EntityVertex>(_vertices, 0, _vertexCount));
        _gpuBuffer.Bind(pass);

        device.Api.RenderPassEncoderDraw(pass, (uint)_vertexCount, 1, 0, 0);
        _vertexCount = 0;
    }

    /// <summary>Mirrors the fixed-function state the queued vertices were posed under.</summary>
    private void UploadState()
    {
        Matrix4X4<float> projection = GLManager.Projection.Top;

        FogState fog = GLManager.Fog;

        _shader.SetUniformMatrix4("projectionMatrix", projection);
        _shader.SetUniform1("textureSampler", 0);
        _shader.SetUniform1("useTexture", _useTexture ? 1 : 0);
        _shader.SetUniform1("entityId",
            _useTexture ? _glTexToLogicalId.GetValueOrDefault(_currentTextureId) : 0);
        _shader.SetUniform1("alphaThreshold", GLManager.EffectiveAlphaThreshold);
        _shader.SetUniform1("fogEnabled", GLManager.FogEnabled ? 1 : 0);
        _shader.SetUniform1("fogMode", (int)fog.Curve);
        _shader.SetUniform1("fogStart", fog.Start);
        _shader.SetUniform1("fogEnd", fog.End);
        _shader.SetUniform1("fogDensity", fog.Density);
        _shader.SetUniform4("fogColor", fog.Color);
    }

    public void Dispose()
    {
        GLManager.RasterStateChanging -= Flush;
        _gl.DeleteBuffer(_vboId);
        _gl.DeleteVertexArray(_vaoId);
        _gpuBuffer?.Dispose();
        _shader.Dispose();
    }
}
