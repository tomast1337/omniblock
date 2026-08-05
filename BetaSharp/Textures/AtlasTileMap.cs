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

    /// <summary>The tile named <paramref name="name" />'s legacy grid position, as <c>x + y * GridWidth</c>.</summary>
    public int IndexOf(string name)
    {
        _indexByName ??= Tiles.ToDictionary(t => t.Name, t => t.X + t.Y * GridWidth);

        return _indexByName.TryGetValue(name, out int index)
            ? index
            : throw new KeyNotFoundException($"No atlas tile named '{name}'.");
    }
}
