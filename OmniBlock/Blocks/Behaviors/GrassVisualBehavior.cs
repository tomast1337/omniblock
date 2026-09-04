using OmniBlock.Blocks.Materials;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Grass rendering: snowy side texture when snow sits on top, and biome-driven green tint.
///     Top/bottom textures are declarative (<c>setTopBottomTextures</c>); this only handles the
///     world-aware pieces.
/// </summary>
public sealed class GrassVisualBehavior(int side, int snowySide) : IBlockVisuals
{
    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side renderSide, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return defaultTexture;

        var materialAbove = reader.GetMaterial(x, y + 1, z);
        return materialAbove != Material.SnowLayer && materialAbove != Material.SnowBlock ? side : snowySide;
    }

    public int GetColorForFace(Block block, int meta, int face, int defaultColor) => face == 1 ? GrassColors.getDefaultColor() : defaultColor;

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        reader.GetBiomeSource().GetBiomesInArea(x, z, 1, 1);
        var temperature = reader.GetBiomeSource().TemperatureMap[0];
        var downfall = reader.GetBiomeSource().DownfallMap[0];
        return GrassColors.getColor(temperature, downfall);
    }
}
