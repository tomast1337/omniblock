using BetaSharp.Items;
using BetaSharp.Worlds.ClientData.Colors;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Tall grass / fern: meta-driven texture and biome-tinted color (meta 0 = dead bush palette,
/// untinted white). Survival is a plain <see cref="PlantSurvivalBehavior"/> on the Physics/Ticker
/// slots — no extra per-tick logic beyond that.
/// </summary>
internal sealed class TallGrassBehavior : IBlockVisuals, IBlockLifecycle
{
    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => meta switch
    {
        1 => block.TextureId,
        2 => block.TextureId + 16 + 1,
        0 => block.TextureId + 16,
        _ => block.TextureId
    };

    public int GetColor(Block block, int meta, int defaultColor) => meta == 0 ? 0xFFFFFF : GrassColors.getDefaultColor();

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        if (meta == 0) return 0xFFFFFF;

        return BiomeTintedColor(reader, x, y, z);
    }

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int knownMeta, int defaultColor)
        => knownMeta == 0 ? 0xFFFFFF : BiomeTintedColor(reader, x, y, z);

    private static int BiomeTintedColor(IBlockReader reader, int x, int y, int z)
    {
        long positionSeed = x * 3129871 + z * 6129781 + y;
        positionSeed = positionSeed * positionSeed * 42317861L + positionSeed * 11L;
        int biomeX = (int)(x + ((positionSeed >> 14) & 31L));
        int biomeY = (int)(y + ((positionSeed >> 19) & 31L));
        int biomeZ = (int)(z + ((positionSeed >> 24) & 31L));
        reader.GetBiomeSource().GetBiomesInArea(biomeX, biomeZ, 1, 1);
        double temperature = reader.GetBiomeSource().TemperatureMap[0];
        double downfall = reader.GetBiomeSource().DownfallMap[0];
        return GrassColors.getColor(temperature, downfall);
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => Random.Shared.Next(8) == 0 ? Item.ByName("seeds").Id : -1;
}
