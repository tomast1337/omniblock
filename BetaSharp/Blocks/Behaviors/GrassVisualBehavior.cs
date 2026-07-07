using BetaSharp.Blocks.Materials;
using BetaSharp.Worlds.ClientData.Colors;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Grass rendering: snowy side texture when snow sits on top, and biome-driven green tint.
///     Top/bottom textures are declarative (<c>setTopBottomTextures</c>); this only handles the
///     world-aware pieces.
/// </summary>
public sealed class GrassVisualBehavior : IBlockVisuals
{
    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return defaultTexture;

        Material materialAbove = reader.GetMaterial(x, y + 1, z);
        return materialAbove != Material.SnowLayer && materialAbove != Material.SnowBlock ? BlockTextures.GrassSide : BlockTextures.GrassSideSnowy;
    }

    public int GetColorForFace(Block block, int meta, int face, int defaultColor) => face == 1 ? GrassColors.getDefaultColor() : defaultColor;

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        reader.GetBiomeSource().GetBiomesInArea(x, z, 1, 1);
        double temperature = reader.GetBiomeSource().TemperatureMap[0];
        double downfall = reader.GetBiomeSource().DownfallMap[0];
        return GrassColors.getColor(temperature, downfall);
    }
}
