using System.Runtime.InteropServices;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.UI;

/// <summary>
///     Batches the interface's quads and draws them through the draw-command seam.
/// </summary>
/// <remarks>
///     Owns no buffer and no program: the quads are written in the same vertex layout the
///     Tessellator produces and submitted as a <see cref="DrawCommand" />, so whichever backend is
///     running draws them the way it draws everything else. Before this it owned a VAO and a GLSL
///     program of its own, which meant the interface existed on OpenGL only.
/// </remarks>
public sealed class UIBatchRenderer : IDisposable
{
    private const int MaxQuads = 2048;
    private const int MaxVertices = MaxQuads * 6;

    /// <summary>Interface quads are flat, unlit and sample the plain 2D texture rather than an array layer.</summary>
    private const int NoArrayLayer = Tessellator.NoArrayLayer;

    private static readonly Dictionary<string, int> s_pathToLogicalId = new(StringComparer.Ordinal);
    private static bool s_propertiesLoaded;
    private readonly Dictionary<uint, int> _texToLogicalId = [];

    private readonly Vertex[] _vertices = new Vertex[MaxVertices];
    private uint _currentTextureId;

    private Matrix4X4<float> _projection = Matrix4X4<float>.Identity;
    private bool _useTexture;
    private int _vertexCount;

    public UIBatchRenderer(GameOptions gameOptions) =>
        // Nothing to build. The parameter stays because the interface constructs this before the
        // draw target exists, and a later shader option belongs here rather than at every caller.
        _ = gameOptions;

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

    public void Dispose()
    {
        // Nothing owned. Kept so the interface can go on disposing what it built.
    }

    public void Begin(Matrix4X4<float> proj)
    {
        _projection = proj;
        _vertexCount = 0;
        _currentTextureId = 0;
        _useTexture = false;
        Blend = BlendMode.Alpha;
    }

    public void End() => Flush();

    public void SetTexture(uint texId)
    {
        if (texId == 0)
        {
            SetNoTexture();
            return;
        }

        if (_useTexture && _currentTextureId == texId) return;
        Flush();
        _currentTextureId = texId;
        _useTexture = true;
    }

    public void RegisterTexture(uint texId, int logicalId) => _texToLogicalId[texId] = logicalId;

    public void RegisterTextureByPath(string assetPath, uint texId)
    {
        EnsurePropertiesLoaded();
        if (s_pathToLogicalId.TryGetValue(assetPath, out var logicalId))
            _texToLogicalId[texId] = logicalId;
    }

    private static void EnsurePropertiesLoaded()
    {
        if (s_propertiesLoaded) return;
        s_propertiesLoaded = true;

        try
        {
            var text = AssetManager.Instance.GetAsset("shaders/ui_textures.properties").GetTextContent();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                var eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                var path = trimmed[..eq].Trim();
                if (int.TryParse(trimmed[(eq + 1)..].Trim(), out var id))
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

        Add(x0, y0, u0, v0, rgba);
        Add(x0, y1, u0, v1, rgba);
        Add(x1, y1, u1, v1, rgba);
        Add(x0, y0, u0, v0, rgba);
        Add(x1, y1, u1, v1, rgba);
        Add(x1, y0, u1, v0, rgba);
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

        Add(tlX, tlY, u0, v0, rgba);
        Add(blX, blY, u0, v1, rgba);
        Add(brX, brY, u1, v1, rgba);
        Add(tlX, tlY, u0, v0, rgba);
        Add(brX, brY, u1, v1, rgba);
        Add(trX, trY, u1, v0, rgba);
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

        Add(x, y, 0f, 0f, topRgba);
        Add(x, y1, 0f, 0f, bottomRgba);
        Add(x1, y1, 0f, 0f, bottomRgba);
        Add(x, y, 0f, 0f, topRgba);
        Add(x1, y1, 0f, 0f, bottomRgba);
        Add(x1, y, 0f, 0f, topRgba);
    }

    private void Add(float x, float y, float u, float v, uint rgba)
    {
        _vertices[_vertexCount++] = new Vertex(x, y, 0.0f, u, v, (int)rgba, 0)
        {
            ArrayLayer = NoArrayLayer,
            Light = Tessellator.FullBrightLight
        };
    }

    public void Flush()
    {
        if (_vertexCount == 0) return;

        // A batch can be interrupted by 3D geometry drawn into the interface — an item icon, the
        // inventory's mob preview — which applies its own state and does not put this one back. So
        // the batch names what it draws under rather than assuming, and it has to name the whole
        // state: turning blend back on alone used to write it behind the applier, leaving the cache
        // claiming blend was off while it was on, and the next draw asking for off got nothing.
        //
        // Everything but the blend, which is the caller's to choose; see <see cref="Blend" />.
        RenderSystem.State.Apply(RenderState.Interface with
        {
            Blend = Blend
        });

        var texture = _useTexture ? Texture2D.Find(_currentTextureId) : null;
        texture?.Bind();

        RenderSystem.GuiTextureId = texture is null
            ? 0
            : _texToLogicalId.GetValueOrDefault(_currentTextureId);

        // The quads are already in interface space, under the projection the interface chose. The
        // ambient matrices belong to whatever was drawing before — a world, a mob preview — so they
        // are replaced for the length of the submission rather than trusted.
        RenderSystem.Projection.Push();
        RenderSystem.ModelView.Push();
        RenderSystem.Projection.Load(_projection);
        RenderSystem.ModelView.LoadIdentity();

        try
        {
            DrawCommand command = new()
            {
                Vertices = MemoryMarshal.AsBytes(_vertices.AsSpan(0, _vertexCount)),
                VertexCount = _vertexCount,
                Topology = DrawTopology.Triangles,
                Channels = texture is null
                    ? VertexChannels.Color
                    : VertexChannels.Color | VertexChannels.Texture,
                Slot = texture is null ? ProgramSlot.Basic : ProgramSlot.Gui
            };

            RenderSystem.DrawTarget.Submit(command);
        }
        finally
        {
            RenderSystem.ModelView.Pop();
            RenderSystem.Projection.Pop();
            _vertexCount = 0;
        }
    }
}
