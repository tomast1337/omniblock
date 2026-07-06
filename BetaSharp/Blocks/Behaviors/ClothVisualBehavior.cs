namespace BetaSharp.Blocks.Behaviors;

/// <summary>Maps wool metadata to the colored-palette texture strip.</summary>
public sealed class ClothVisualBehavior : IBlockVisuals
{
    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (meta == 0)
        {
            return defaultTexture;
        }

        meta = ~(meta & 15);
        return BlockTextures.WoolColoredPaletteBase + ((meta & 8) >> 3) + (meta & 7) * 16;
    }

    public static int getBlockMeta(int itemMeta) => ~itemMeta & 15;

    public static int getItemMeta(int blockMeta) => ~blockMeta & 15;
}
