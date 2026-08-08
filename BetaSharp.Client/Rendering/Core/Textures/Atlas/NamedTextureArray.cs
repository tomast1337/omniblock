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
///     its own <see cref="AtlasTileMap" /> and its own <see cref="TextureArray" />. A pack override
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

    private readonly Dictionary<string, TextureSource> _sourceByName = [];

    public TextureArray? Texture { get; private set; }
    public int LayerSize { get; private set; }

    public NamedTextureArray(string domain, AtlasTileMap tileMap, Func<Image<Rgba32>> loadDefaultGridImage, Func<TexturePack> activePack)
    {
        _domain = domain;
        _tileMap = tileMap;
        _loadDefaultGridImage = loadDefaultGridImage;
        _activePack = activePack;
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

        var resolved = new ResolvedTexture[_tileMap.LayerCount];
        int targetSize = _tileMap.TileSize;

        resolved[AtlasTileMap.MissingLayer] = new ResolvedTexture(
            TextureSource.Fallback, MissingTextureImage.Generate(_tileMap.TileSize));

        for (int layer = 1; layer < resolved.Length; layer++)
        {
            string name = _tileMap.Tiles[layer - 1].Name;
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
                if (i != AtlasTileMap.MissingLayer) _sourceByName[_tileMap.Tiles[i - 1].Name] = resolved[i].Source;
            }

            Texture ??= new TextureArray($"NamedTextureArray[{_domain}]");
            fixed (byte* ptr = packed)
            {
                Texture.Upload(targetSize, targetSize, resolved.Length, ptr);
            }
            Texture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            // Repeat rather than clamp, for the one caller that runs past a layer's edge: flowing
            // water turns its quad about the tile's corner. Beta answered that by writing the frame
            // into a 2x2 block of atlas cells, which a layer wrapping onto itself reproduces exactly.
            // Nothing can bleed into a neighbouring texture either way — that is what a layer buys.
            Texture.SetWrap(TextureWrapMode.Repeat, TextureWrapMode.Repeat);
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

    // ---- Inspector API ----

    public IReadOnlyList<string> Names => [.. _tileMap.Tiles.Select(t => t.Name)];

    public TextureSource GetSource(string name) => _sourceByName.TryGetValue(name, out TextureSource source) ? source : TextureSource.Fallback;

    public IEnumerable<string> Search(string substring) =>
        _tileMap.Tiles.Select(t => t.Name).Where(n => n.Contains(substring, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => Texture?.Dispose();
}
