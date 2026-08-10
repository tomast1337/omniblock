using System.Runtime.InteropServices;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
/// Batches posed/lit entity model geometry (baked by <see cref="Models.ModelPart"/>) and draws it
/// through the draw-command seam.
/// </summary>
public sealed class EntityBatchRenderer : IDisposable
{
    private static EntityBatchRenderer? s_instance;
    public static EntityBatchRenderer Instance =>
        s_instance ?? throw new InvalidOperationException($"{nameof(EntityBatchRenderer)}.{nameof(Initialize)} must be called before use.");

    public static void Initialize(GameOptions options) => s_instance ??= new EntityBatchRenderer(options);

    private const int MaxVertices = 65536;

    private readonly EntityVertex[] _vertices = new EntityVertex[MaxVertices];
    private readonly Dictionary<uint, int> _glTexToLogicalId = [];
    private readonly Vertex[] _seamVertices = new Vertex[MaxVertices];

    private int _vertexCount;
    private uint _currentTextureId;
    private bool _useTexture;
    private bool _active;

    private EntityBatchRenderer(GameOptions options)
    {
        // Queued geometry must be drawn under the blend, depth and alpha state it was posed with,
        // and renderers flip that state freely between parts of the same mob.
        GLManager.RasterStateChanging += Flush;
    }

    /// <summary>What the caller has bound.</summary>
    private static uint BoundTextureId => Texture2D.Bound?.Id ?? 0;

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

    public void Flush()
    {
        if (_vertexCount == 0) return;

        FlushThroughSeam(_seamVertices);
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

    public void Dispose()
    {
        GLManager.RasterStateChanging -= Flush;
    }
}
