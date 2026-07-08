using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

namespace BetaSharp.Blocks;

internal class BlockGlowstone(int i, int j, Material material) : Block(i, j, material)
{
    private static readonly int s_glowstoneDustId = Item.ByName("yellow_dust").Id;

    public override int getDroppedItemCount() => 2 + Random.Shared.Next(3);

    public override int getDroppedItemId(int blockMeta) => s_glowstoneDustId;
}
