using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks;

/// <summary>
/// Composable capability for stateless visual overrides: metadata-driven textures,
/// biome-driven color multipliers, and adjacent-face culling.
/// <para>
/// Every hook receives the base logic's result as a trailing <c>default*</c> parameter and
/// returns the primitive directly — no <see cref="Nullable{T}"/> on the hot path.
/// </para>
/// </summary>
public interface IBlockVisuals
{
    int GetTexture(Block block, Side side, int defaultTexture) => defaultTexture;
    int GetTexture(Block block, Side side, int meta, int defaultTexture) => defaultTexture;
    int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture) => defaultTexture;

    int GetColor(Block block, int meta, int defaultColor) => defaultColor;
    int GetColorForFace(Block block, int meta, int face, int defaultColor) => defaultColor;
    int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor) => defaultColor;
    int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int knownMeta, int defaultColor) => defaultColor;

    bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility) => defaultVisibility;
}
