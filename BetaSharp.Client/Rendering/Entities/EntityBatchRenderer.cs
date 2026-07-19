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
    public static readonly EntityBatchRenderer Instance = new();

    private const int MaxVertices = 65536;

    private readonly Shader _shader;
    private readonly GL _silkGL;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly EntityVertex[] _vertices = new EntityVertex[MaxVertices];

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private bool _active;

    private EntityBatchRenderer()
    {
        _shader = new Shader(
            AssetManager.Instance.getAsset("shaders/entity_batch.vert").GetTextContent(),
            AssetManager.Instance.getAsset("shaders/entity_batch.frag").GetTextContent());
        _silkGL = ((LegacyGL)GLManager.GL).SilkGL;

        _vaoId = _silkGL.GenVertexArray();
        _vboId = _silkGL.GenBuffer();

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _vboId);
        _silkGL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVertices * sizeof(EntityVertex)), null, BufferUsageARB.StreamDraw);

        _silkGL.EnableVertexAttribArray(0);
        _silkGL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, (void*)0);

        _silkGL.EnableVertexAttribArray(1);
        _silkGL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 24, (void*)12);

        _silkGL.EnableVertexAttribArray(2);
        _silkGL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 24, (void*)20);

        _silkGL.BindVertexArray(0);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    public void Begin(Matrix4X4<float> projection)
    {
        _active = true;
        _vertexCount = 0;
        _currentTextureId = 0;
        _useTexture = true;

        EmulatedGL gl = (EmulatedGL)GLManager.GL;
        EntityFogSnapshot fog = gl.GetFogState();

        _shader.Bind();
        _shader.SetUniformMatrix4("projectionMatrix", projection);
        _shader.SetUniform1("textureSampler", 0);
        _shader.SetUniform1("alphaThreshold", gl.GetCurrentAlphaThreshold());
        _shader.SetUniform1("fogEnabled", fog.Enabled ? 1 : 0);
        _shader.SetUniform1("fogMode", fog.Mode);
        _shader.SetUniform1("fogStart", fog.Start);
        _shader.SetUniform1("fogEnd", fog.End);
        _shader.SetUniform1("fogDensity", fog.Density);
        _shader.SetUniform4("fogColor", fog.Color);
        GLManager.GL.UseProgram(0);
    }

    public void End()
    {
        Flush();
        _active = false;
    }

    public void SetTexture(uint texId)
    {
        if (!_active || (_useTexture && texId == _currentTextureId)) return;
        Flush();
        _currentTextureId = texId;
        _useTexture = true;
    }

    /// <summary>Used for solid-color passes (hurt flash, damage overlay) that render posed model geometry without sampling a texture.</summary>
    public void SetNoTexture()
    {
        if (!_active || !_useTexture) return;
        Flush();
        _useTexture = false;
    }

    public void SubmitTriangles(ReadOnlySpan<EntityVertex> verts)
    {
        if (!_active) return;

        if (_vertexCount + verts.Length > MaxVertices)
        {
            Flush();
        }

        verts.CopyTo(_vertices.AsSpan(_vertexCount));
        _vertexCount += verts.Length;
    }

    public void Flush()
    {
        if (_vertexCount == 0) return;

        GLManager.GL.UseProgram(_shader.ProgramId);
        _shader.SetUniform1("useTexture", _useTexture ? 1 : 0);

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
        _vertexCount = 0;

        GLManager.GL.UseProgram(0);
    }

    public void Dispose()
    {
        _silkGL.DeleteBuffer(_vboId);
        _silkGL.DeleteVertexArray(_vaoId);
        _shader.Dispose();
    }
}
