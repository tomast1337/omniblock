using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks;

/// <summary>
///     Composable capability for stateless visual overrides: metadata-driven textures,
///     biome-driven color multipliers, and adjacent-face culling.
///     <para>
///         Every hook receives the base logic's result as a trailing <c>default*</c> parameter and
///         returns the primitive directly, no <see cref="Nullable{T}" /> on the hot path.
///     </para>
/// </summary>
public interface IBlockVisuals
{
    int GetTexture(Block block, Side side, int defaultTexture)
    {
        return defaultTexture;
    }

    int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        return defaultTexture;
    }

    int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        return defaultTexture;
    }

    int GetColor(Block block, int meta, int defaultColor)
    {
        return defaultColor;
    }

    int GetColorForFace(Block block, int meta, int face, int defaultColor)
    {
        return defaultColor;
    }

    int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        return defaultColor;
    }

    int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int knownMeta, int defaultColor)
    {
        return defaultColor;
    }

    bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return defaultVisibility;
    }

    /// <summary>
    ///     Overrides opacity for blocks whose transparency is a runtime toggle (e.g. leaves under
    ///     fancy/fast graphics) rather than a fixed construction-time value.
    /// </summary>
    bool IsOpaque(Block block, bool defaultOpaque)
    {
        return defaultOpaque;
    }

    /// <summary>
    ///     Overrides the light level a block emits/reflects at a position (e.g. fluids sample the brighter of this and
    ///     the cell above).
    /// </summary>
    float GetLuminance(Block block, ILightProvider lighting, int x, int y, int z, float defaultLuminance)
    {
        return defaultLuminance;
    }

    /// <inheritdoc cref="Block.GetLightLevels" />
    LightLevels GetLightLevels(Block block, ILightProvider lighting, int x, int y, int z, LightLevels defaultLevels)
    {
        return defaultLevels;
    }
}