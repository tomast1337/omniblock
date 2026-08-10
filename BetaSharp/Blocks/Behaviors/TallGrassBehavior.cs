using OmniBlock.Items;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Tall grass / fern: meta-driven texture and biome-tinted color (meta 0 = dead bush palette,
///     untinted white). Survival is a plain <see cref="PlantSurvivalBehavior" /> on the Physics/Ticker
///     slots.
///     <para>
///         Rare-drop item is a required, (see <c>BehaviorRegistry</c>'s <c>"tall_grass"</c> entry).
///     </para>
///     <para>
///         Seed drop chance (<paramref name="seedDropChanceOneIn" />, 1-in-N per break) is also a
///         required.
///     </para>
/// </summary>
internal sealed class TallGrassBehavior(Item seeds, int seedDropChanceOneIn, int[] textures) : IBlockVisuals, IBlockLifecycle
{
    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => Random.Shared.Next(seedDropChanceOneIn) == 0 ? seeds.Id : -1;

    // Metadata past the last kind kept the middle one -- plain tall grass -- rather than failing.
    public int GetTexture(Block block, Side side, int meta, int defaultTexture) =>
        meta >= 0 && meta < textures.Length ? textures[meta] : textures[1];

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
}
