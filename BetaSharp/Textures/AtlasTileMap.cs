using System.Reflection;
using System.Text.Json;

namespace BetaSharp.Textures;

/// <summary>A single named cell in a legacy grid atlas, in tile (not pixel) coordinates.</summary>
public sealed record AtlasTile(string Name, int X, int Y);

/// <summary>
///     The default grid layout of a legacy 16x16 atlas (<c>terrain.png</c>, <c>gui/items.png</c>).
/// </summary>
/// <remarks>
///     Beta 1.7.3's grid position for a tile never changes, so this is build-time schema, not pack
///     content — a pack overrides a texture by name (see <c>NamedTextureArray</c>), never by
///     shipping a new grid. Kept as JSON per the "data over code" convention rather than a hand-written
///     table like the <c>BlockTextures</c> constants this replaces. Lives in the core project, not the
///     client, because item/block definition loading resolves a name to a grid index too — the
///     legacy int a <c>TextureId</c> used to spell out directly — and that loading has no client
///     dependency to reach the GPU-side atlas machinery through.
/// </remarks>
public sealed class AtlasTileMap
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

    private Dictionary<string, int>? _indexByName;
    private int[]? _layerByGridIndex;

    /// <summary>
    ///     The layer a renderer lands on when it asks for a cell no tile is named after. Reserved so
    ///     that looks broken rather than like whichever tile happened to be listed first.
    /// </summary>
    public const int MissingLayer = 0;

    public int TileSize { get; init; } = 16;
    public int GridWidth { get; init; } = 16;
    public int GridHeight { get; init; } = 16;
    public IReadOnlyList<AtlasTile> Tiles { get; init; } = [];

    public static AtlasTileMap Parse(string json) =>
        JsonSerializer.Deserialize<AtlasTileMap>(json, s_options)
        ?? throw new InvalidDataException("Atlas tile map JSON deserialized to null.");

    /// <summary>
    ///     Reads straight from the assembly manifest rather than through <see cref="AssetManager" />:
    ///     that requires <c>b1.7.3.jar</c> on disk to construct at all, which item/block definition
    ///     loading — the dedicated server included — must not depend on for grid-position metadata
    ///     that was compiled in.
    /// </summary>
    public static AtlasTileMap Load(string embeddedAssetPath)
    {
        string resourceName = $"{nameof(BetaSharp)}.{embeddedAssetPath.Replace('/', '.')}";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    /// <summary>
    ///     The tile named <paramref name="name" />'s legacy grid position, as <c>x + y * GridWidth</c>.
    ///     The name may carry a namespace (<c>betasharp:stone</c>) or not — tiles are keyed by path
    ///     alone, since an atlas is not itself namespaced.
    /// </summary>
    public int IndexOf(string name)
    {
        _indexByName ??= Tiles.ToDictionary(t => t.Name, t => t.X + t.Y * GridWidth);

        if (name.Contains(':', StringComparison.Ordinal))
        {
            name = ResourceLocation.Parse(name).Path;
        }

        return _indexByName.TryGetValue(name, out int index)
            ? index
            : throw new KeyNotFoundException($"No atlas tile named '{name}'.");
    }

    /// <summary>How many texture-array layers this map needs, the reserved one included.</summary>
    public int LayerCount => Tiles.Count + 1;

    /// <summary>
    ///     The array layer holding <paramref name="name" />: its position in <see cref="Tiles" />,
    ///     offset past the reserved <see cref="MissingLayer" />.
    /// </summary>
    /// <remarks>
    ///     Layers are not grid positions, deliberately. A tile listed past the 256th cell of a 16x16
    ///     map still gets a layer, which is what lets a definition from outside this assembly ship a
    ///     texture the legacy grid has no room for.
    /// </remarks>
    public int LayerOf(string name) => LayerOfGridIndex(IndexOf(name));

    /// <summary>
    ///     The array layer for a legacy grid index — <c>x + y * GridWidth</c>, the int a
    ///     <c>TextureId</c> still carries — or <see cref="MissingLayer" /> for an unclaimed cell.
    /// </summary>
    public int LayerOfGridIndex(int gridIndex)
    {
        _layerByGridIndex ??= BuildGridLayers();

        return (uint)gridIndex < (uint)_layerByGridIndex.Length ? _layerByGridIndex[gridIndex] : MissingLayer;
    }

    private int[] BuildGridLayers()
    {
        int[] layers = new int[GridWidth * GridHeight];

        for (int i = 0; i < Tiles.Count; i++)
        {
            AtlasTile tile = Tiles[i];
            int gridIndex = tile.X + tile.Y * GridWidth;

            if ((uint)gridIndex < (uint)layers.Length) layers[gridIndex] = i + 1;
        }

        return layers;
    }
}
