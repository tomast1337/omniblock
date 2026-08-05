using System.Text.Json;

namespace BetaSharp.Client.Rendering.Core.Textures.Atlas;

/// <summary>A single named cell in a legacy grid atlas, in tile (not pixel) coordinates.</summary>
public sealed record AtlasTile(string Name, int X, int Y);

/// <summary>
///     The default grid layout of a legacy 16x16 atlas (<c>terrain.png</c>, <c>gui/items.png</c>).
/// </summary>
/// <remarks>
///     Beta 1.7.3's grid position for a tile never changes, so this is build-time schema, not pack
///     content — a pack overrides a texture by name (see <see cref="NamedTextureArray" />), never by
///     shipping a new grid. Kept as JSON per the "data over code" convention rather than a hand-written
///     table like the <c>BlockTextures</c> constants this replaces.
/// </remarks>
public sealed class AtlasTileMap
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

    public int TileSize { get; init; } = 16;
    public int GridWidth { get; init; } = 16;
    public int GridHeight { get; init; } = 16;
    public IReadOnlyList<AtlasTile> Tiles { get; init; } = [];

    public static AtlasTileMap Parse(string json) =>
        JsonSerializer.Deserialize<AtlasTileMap>(json, s_options)
        ?? throw new InvalidDataException("Atlas tile map JSON deserialized to null.");

    public static AtlasTileMap Load(string embeddedAssetPath) =>
        Parse(AssetManager.Instance.getAsset(embeddedAssetPath).GetTextContent());
}
