namespace BetaSharp.Textures;

/// <summary>
///     The two legacy grid atlases a definition can name a texture in.
/// </summary>
/// <remarks>
///     An atlas index on its own carries no atlas identity — the same <c>int</c> meant a tile in
///     <c>terrain.png</c> when a block produced it and a tile in <c>gui/items.png</c> when an item
///     did, disambiguated only by which code path read it. Resolving through the map that owns the
///     name puts that back in the type.
/// </remarks>
public static class Atlases
{
    public static AtlasTileMap Terrain { get; } = AtlasTileMap.Load("textures/atlas/terrain.json");

    public static AtlasTileMap Items { get; } = AtlasTileMap.Load("textures/atlas/items.json");
}
