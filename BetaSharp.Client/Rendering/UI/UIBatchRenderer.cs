using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Core.WebGPU;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.UI;

public sealed class UIBatchRenderer : IDisposable
{
    private const int MaxQuads = 2048;
    private const int MaxVertices = MaxQuads * 6;

    private readonly UIShader _shader;
    private readonly IGL _gl;
    private readonly uint _vaoId;
    private readonly uint _vboId;
    private readonly UIVertex[] _vertices = new UIVertex[MaxVertices];

    // WebGPU path
    private WgpuDynamicBuffer? _gpuBuffer;

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private readonly Dictionary<uint, int> _glTexToLogicalId = new();

    private static readonly Dictionary<string, int> s_pathToLogicalId = new();
    private static bool s_propertiesLoaded;

    public unsafe UIBatchRenderer(GameOptions gameOptions)
    {
        _shader = new UIShader(gameOptions);
        _gl = GLManager.GL;

        _vaoId = _gl.GenVertexArray();
        _vboId = _gl.GenBuffer();

        _gl.BindVertexArray(_vaoId);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);
        _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(MaxVertices * sizeof(UIVertex)), null, GLEnum.StreamDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, GLEnum.Float, false, 20, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, 20, (void*)8);

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, 20, (void*)16);

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
    }

    /// <summary>
    ///     The blend the interface has selected, which queued geometry is drawn under.
    /// </summary>
    /// <remarks>
    ///     Held here because a flush has to restate the whole interface state — 3D geometry drawn
    ///     into the interface, an item icon or the inventory's mob preview, applies its own and does
    ///     not put this one back — and restating it must not silently undo a blend a control chose.
    ///     The vignette is the case that proves it: it selects <see cref="BlendMode.Darken" /> and
    ///     queues a full-screen quad, so a flush that reset the blend to alpha drew it as opaque
    ///     black over the world rather than darkening what was already there.
    /// </remarks>
    public BlendMode Blend { get; set; } = BlendMode.Alpha;

    public void Begin(Matrix4X4<float> proj)
    {
        GLManager.GL.UseProgram(_shader.ProgramId);
        _shader.SetProjection(proj);
        GLManager.GL.UseProgram(0);
        _vertexCount = 0;
        _currentTextureId = 0;
        _useTexture = false;
        Blend = BlendMode.Alpha;
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

        // A batch can be interrupted by 3D geometry drawn into the interface — an item icon, the
        // inventory's mob preview — which applies its own state and does not put this one back. So
        // the batch names what it draws under rather than assuming, and it has to name the whole
        // state: turning blend back on alone used to write it behind the applier, leaving the cache
        // claiming blend was off while it was on, and the next draw asking for off got nothing.
        //
        // Everything but the blend, which is the caller's to choose; see <see cref="Blend" />.
        GLManager.State.Apply(RenderState.Interface with { Blend = Blend });

        GLManager.GL.UseProgram(_shader.ProgramId);
        _shader.SetUseTexture(_useTexture);

        int logicalTexId = _useTexture && _glTexToLogicalId.TryGetValue(_currentTextureId, out int id) ? id : 0;
        _shader.SetTextureId(logicalTexId);

        if (_useTexture && _currentTextureId != 0)
        {
            _gl.ActiveTexture(GLEnum.Texture0);
            _gl.BindTexture(GLEnum.Texture2D, _currentTextureId);
        }

        _gl.BindVertexArray(_vaoId);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vboId);

        _gl.BufferSubData(GLEnum.ArrayBuffer, 0, new ReadOnlySpan<UIVertex>(_vertices, 0, _vertexCount));

        _gl.DrawArrays(GLEnum.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
        _vertexCount = 0;

        GLManager.GL.UseProgram(0);
    }

    /// <summary>
    ///     Draws queued UI geometry through the native WebGPU command encoder.
    ///     The caller has already uploaded the projection and texture uniforms.
    /// </summary>
    public unsafe void FlushWebGpu(RenderPassEncoder* pass, WgpuPipeline pipeline)
    {
        if (_vertexCount == 0) return;

        WebGpuDevice device = WebGpuDevice.Current!;
        _gpuBuffer ??= new WgpuDynamicBuffer(device, (ulong)(MaxVertices * sizeof(UIVertex)));

        _gpuBuffer.Write(new ReadOnlySpan<UIVertex>(_vertices, 0, _vertexCount));
        _gpuBuffer.Bind(pass);

        device.Api.RenderPassEncoderDraw(pass, (uint)_vertexCount, 1, 0, 0);
        _vertexCount = 0;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vaoId);
        _gl.DeleteBuffer(_vboId);
        _gpuBuffer?.Dispose();
        _shader.Dispose();
    }
}
