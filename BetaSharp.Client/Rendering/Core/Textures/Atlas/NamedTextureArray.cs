using BetaSharp.Client.Resource.Pack;
using BetaSharp.Textures;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BetaSharp.Client.Rendering.Core.Textures.Atlas;

/// <summary>
///     A GPU texture array addressed by name instead of grid position, with the fallback chain
///     Custom Pack → Default → Fallback checkerboard behind every name.
/// </summary>
/// <remarks>
///     One instance per domain — <c>terrain</c>, <c>items</c>, and any future one — each backed by
///     its own <see cref="AtlasTileMap" /> and its own <see cref="GLTextureArray" />. A pack override
///     lives at <c>textures/&lt;domain&gt;/&lt;name&gt;.png</c>; resolution mismatches rebuild every
///     layer at the largest size seen (see <see cref="Rebuild" />) rather than silently losing detail
///     on a high-resolution pack.
/// </remarks>
public sealed class NamedTextureArray : IDisposable
{
    private readonly string _domain;
    private readonly AtlasTileMap _tileMap;
    private readonly Func<Image<Rgba32>> _loadDefaultGridImage;
    private readonly Func<TexturePack> _activePack;

    private readonly Dictionary<string, int> _layerByName = [];
    private readonly List<string> _names = [];
    private readonly int[] _layerByGridIndex;
    private readonly Dictionary<string, TextureSource> _sourceByName = [];

    /// <summary>
    ///     The checkerboard, reserved so a grid cell no tile is named after has somewhere to land.
    ///     A cell addressed by a legacy index nothing claims should look broken rather than like
    ///     whichever tile happened to be first in the map.
    /// </summary>
    public const int MissingLayer = 0;

    public GLTextureArray? Texture { get; private set; }
    public int LayerSize { get; private set; }
    public int LayerCount => _names.Count + 1;

    public NamedTextureArray(string domain, AtlasTileMap tileMap, Func<Image<Rgba32>> loadDefaultGridImage, Func<TexturePack> activePack)
    {
        _domain = domain;
        _tileMap = tileMap;
        _loadDefaultGridImage = loadDefaultGridImage;
        _activePack = activePack;

        _layerByGridIndex = new int[tileMap.GridWidth * tileMap.GridHeight];

        foreach (AtlasTile tile in tileMap.Tiles)
        {
            int layer = _names.Count + 1;
            _layerByName[tile.Name] = layer;
            _names.Add(tile.Name);
            _layerByGridIndex[tile.X + tile.Y * tileMap.GridWidth] = layer;
        }
    }

    /// <summary>
    ///     Re-resolves every name and re-uploads the array. Called once at startup and again whenever
    ///     the active texture pack changes.
    /// </summary>
    public unsafe void Rebuild()
    {
        using Image<Rgba32> defaultGrid = _loadDefaultGridImage();
        Dictionary<string, Image<Rgba32>> defaults = AtlasSlicer.Slice(defaultGrid, _tileMap);
        TexturePack pack = _activePack();

        var resolved = new ResolvedTexture[LayerCount];
        int targetSize = _tileMap.TileSize;

        resolved[MissingLayer] = new ResolvedTexture(TextureSource.Fallback, MissingTextureImage.Generate(_tileMap.TileSize));

        for (int layer = 1; layer < LayerCount; layer++)
        {
            string name = _names[layer - 1];
            resolved[layer] = TextureFallbackChain.Resolve(
                name,
                n => pack.GetResourceAsStream($"textures/{_domain}/{n}.png"),
                defaults,
                _tileMap.TileSize);
            targetSize = Math.Max(targetSize, resolved[layer].Image.Width);
        }

        try
        {
            LayerSize = targetSize;
            int layerBytes = targetSize * targetSize * 4;
            byte[] packed = new byte[layerBytes * resolved.Length];

            for (int i = 0; i < resolved.Length; i++)
            {
                using Image<Rgba32> layer = ResizeToTarget(resolved[i].Image, targetSize);
                layer.CopyPixelDataTo(packed.AsSpan(i * layerBytes, layerBytes));
                if (i != MissingLayer) _sourceByName[_names[i - 1]] = resolved[i].Source;
            }

            Texture ??= new GLTextureArray($"NamedTextureArray[{_domain}]");
            fixed (byte* ptr = packed)
            {
                Texture.Upload(targetSize, targetSize, resolved.Length, ptr);
            }
            Texture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Texture.SetWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
        }
        finally
        {
            foreach (ResolvedTexture r in resolved) r.Image.Dispose();
            foreach (Image<Rgba32> d in defaults.Values) d.Dispose();
        }
    }

    /// <summary>
    ///     Always clones rather than mutating in place — the source stays independently owned by
    ///     whoever resolved it, so <see cref="Rebuild" /> can dispose it once without risking a
    ///     double-dispose of whatever this returns.
    /// </summary>
    private static Image<Rgba32> ResizeToTarget(Image<Rgba32> image, int targetSize)
    {
        if (image.Width == targetSize && image.Height == targetSize) return image.Clone();
        return image.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(targetSize, targetSize),
            Sampler = KnownResamplers.NearestNeighbor
        }));
    }

    /// <summary>
    ///     The layer for a legacy grid index — <c>x + y * GridWidth</c>, the int a <c>TextureId</c>
    ///     still carries — or <see cref="MissingLayer" /> for a cell no tile is named after.
    /// </summary>
    /// <remarks>
    ///     Layers are not grid positions, deliberately: a tile added past the 256th cell has a layer
    ///     but no grid index, which is what lets an outside definition ship a texture the legacy
    ///     atlas has no room for. Renderers that still address by index come through here; ones that
    ///     address by name use <see cref="GetLayer" />.
    /// </remarks>
    public int LayerOfGridIndex(int gridIndex) =>
        (uint)gridIndex < (uint)_layerByGridIndex.Length ? _layerByGridIndex[gridIndex] : MissingLayer;

    // ---- Inspector API ----

    public IReadOnlyList<string> Names => _names;

    public int? GetLayer(string name) => _layerByName.TryGetValue(name, out int layer) ? layer : null;

    public TextureSource GetSource(string name) => _sourceByName.TryGetValue(name, out TextureSource source) ? source : TextureSource.Fallback;

    public IEnumerable<string> Search(string substring) =>
        _names.Where(n => n.Contains(substring, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => Texture?.Dispose();
}
