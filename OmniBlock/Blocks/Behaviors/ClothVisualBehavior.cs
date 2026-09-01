namespace OmniBlock.Blocks.Behaviors;

/// <summary>Picks the wool texture for a colour, by block metadata.</summary>
/// <remarks>
///     The colours are a plain list in metadata order rather than a base index walked with grid
///     arithmetic. The tiles happen to sit in two columns of eight on <c>terrain.png</c>, which the
///     arithmetic encoded as <c>base + ((~meta &amp; 8) >> 3) + (~meta &amp; 7) * 16</c> — a layout
///     accident no definition outside this assembly could have reproduced, and that a seventeenth
///     colour would have had nowhere to land in.
/// </remarks>
public sealed class ClothVisualBehavior(int[] textures) : IBlockVisuals
{
    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        return textures[meta & 15];
    }

    public static int GetBlockMeta(int itemMeta)
    {
        return ~itemMeta & 15;
    }

    public static int GetItemMeta(int blockMeta)
    {
        return ~blockMeta & 15;
    }
}