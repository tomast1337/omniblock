using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.UI;

public sealed class UIBatchRenderer : IDisposable
{
    private const int MaxQuads = 2048;
    private const int MaxVertices = MaxQuads * 6;

    private readonly UIShader _shader;
    private readonly GL _silkGL;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly UIVertex[] _vertices = new UIVertex[MaxVertices];

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private readonly Dictionary<uint, int> _glTexToLogicalId = new();

    private static readonly Dictionary<string, int> s_pathToLogicalId = new();
    private static bool s_propertiesLoaded;

    public unsafe UIBatchRenderer(GameOptions gameOptions)
    {
        _shader = new UIShader(gameOptions);
        _silkGL = ((LegacyGL)GLManager.GL).SilkGL;

        _vaoId = _silkGL.GenVertexArray();
        _vboId = _silkGL.GenBuffer();

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _vboId);
        _silkGL.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVertices * sizeof(UIVertex)), null, BufferUsageARB.StreamDraw);

        _silkGL.EnableVertexAttribArray(0);
        _silkGL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 20, (void*)0);

        _silkGL.EnableVertexAttribArray(1);
        _silkGL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 20, (void*)8);

        _silkGL.EnableVertexAttribArray(2);
        _silkGL.VertexAttribPointer(2, 4, VertexAttribPointerType.UnsignedByte, true, 20, (void*)16);

        _silkGL.BindVertexArray(0);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
    }

    public void Begin(Matrix4X4<float> proj)
    {
        _silkGL.Enable(EnableCap.Blend);
        GLManager.GL.UseProgram(_shader.ProgramId);
        _shader.SetProjection(proj);
        GLManager.GL.UseProgram(0);
        _vertexCount = 0;
        _currentTextureId = 0;
        _useTexture = false;
    }

    public void End() => Flush();

    public void SetTexture(uint texId)
    {
        if (texId == 0) { SetNoTexture(); return; }
        if (_useTexture && _currentTextureId == texId) return;
        Flush();
        _currentTextureId = texId;
        _useTexture = true;
    }

    public void RegisterTexture(uint glTexId, int logicalId)
    {
        _glTexToLogicalId[glTexId] = logicalId;
    }

    public void RegisterTextureByPath(string assetPath, uint glTexId)
    {
        EnsurePropertiesLoaded();
        if (s_pathToLogicalId.TryGetValue(assetPath, out int logicalId))
            _glTexToLogicalId[glTexId] = logicalId;
    }

    private static void EnsurePropertiesLoaded()
    {
        if (s_propertiesLoaded) return;
        s_propertiesLoaded = true;

        try
        {
            string text = AssetManager.Instance.getAsset("shaders/ui_textures.properties").GetTextContent();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                string path = trimmed[..eq].Trim();
                if (int.TryParse(trimmed[(eq + 1)..].Trim(), out int id))
                    s_pathToLogicalId[path] = id;
            }
        }
        catch
        {
            // Properties file missing or unparseable — all textures map to ID 0 (passthrough).
            // Shader operates identically to pre-texture-ID behavior.
        }
    }

    private void SetNoTexture()
    {
        if (!_useTexture) return;
        Flush();
        _useTexture = false;
    }

    internal void AddQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, uint rgba)
    {
        if (_vertexCount + 6 > MaxVertices)
            Flush();

        _vertices[_vertexCount++] = new UIVertex { X = x0, Y = y0, U = u0, V = v0, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = x0, Y = y1, U = u0, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y1, U = u1, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = x0, Y = y0, U = u0, V = v0, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y1, U = u1, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y0, U = u1, V = v0, Rgba = rgba };
    }

    internal void AddQuadCorners(
        float tlX, float tlY,
        float blX, float blY,
        float brX, float brY,
        float trX, float trY,
        float u0, float v0, float u1, float v1, uint rgba)
    {
        if (_vertexCount + 6 > MaxVertices)
            Flush();

        _vertices[_vertexCount++] = new UIVertex { X = tlX, Y = tlY, U = u0, V = v0, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = blX, Y = blY, U = u0, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = brX, Y = brY, U = u1, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = tlX, Y = tlY, U = u0, V = v0, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = brX, Y = brY, U = u1, V = v1, Rgba = rgba };
        _vertices[_vertexCount++] = new UIVertex { X = trX, Y = trY, U = u1, V = v0, Rgba = rgba };
    }

    public void AddColoredQuad(float x, float y, float w, float h, uint rgba)
    {
        SetNoTexture();
        AddQuad(x, y, x + w, y + h, 0f, 0f, 0f, 0f, rgba);
    }

    public void AddGradientQuad(float x, float y, float w, float h, uint topRgba, uint bottomRgba)
    {
        SetNoTexture();

        if (_vertexCount + 6 > MaxVertices)
            Flush();

        float x1 = x + w, y1 = y + h;

        _vertices[_vertexCount++] = new UIVertex { X = x, Y = y, U = 0, V = 0, Rgba = topRgba };
        _vertices[_vertexCount++] = new UIVertex { X = x, Y = y1, U = 0, V = 0, Rgba = bottomRgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y1, U = 0, V = 0, Rgba = bottomRgba };
        _vertices[_vertexCount++] = new UIVertex { X = x, Y = y, U = 0, V = 0, Rgba = topRgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y1, U = 0, V = 0, Rgba = bottomRgba };
        _vertices[_vertexCount++] = new UIVertex { X = x1, Y = y, U = 0, V = 0, Rgba = topRgba };
    }

    public unsafe void Flush()
    {
        if (_vertexCount == 0) return;

        // Legacy GL calls between flushes (e.g. 3D block rendering) can disable blend.
        // Restore it here so transparent font atlas pixels are not written as opaque black.
        _silkGL.Enable(EnableCap.Blend);

        GLManager.GL.UseProgram(_shader.ProgramId);
        _shader.SetUseTexture(_useTexture);

        int logicalTexId = _useTexture && _glTexToLogicalId.TryGetValue(_currentTextureId, out int id) ? id : 0;
        _shader.SetTextureId(logicalTexId);

        if (_useTexture && _currentTextureId != 0)
        {
            _silkGL.ActiveTexture(TextureUnit.Texture0);
            _silkGL.BindTexture(TextureTarget.Texture2D, _currentTextureId);
        }

        _silkGL.BindVertexArray(_vaoId);
        _silkGL.BindBuffer(BufferTargetARB.ArrayBuffer, _vboId);

        fixed (UIVertex* ptr = _vertices)
        {
            _silkGL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_vertexCount * sizeof(UIVertex)), ptr);
        }

        _silkGL.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _silkGL.BindVertexArray(0);
        _vertexCount = 0;

        GLManager.GL.UseProgram(0);
    }

    public void Dispose()
    {
        _silkGL.DeleteVertexArray(_vaoId);
        _silkGL.DeleteBuffer(_vboId);
        _shader.Dispose();
    }
}
