using BetaSharp.Items;

namespace BetaSharp.Blocks;

internal class BlockGravel(int id, int textureIndex) : BlockSand(id, textureIndex)
{
    private static readonly int s_flintId = Item.ByName("flint").Id;

    public override int getDroppedItemId(int blockMeta) => Random.Shared.Next(10) == 0 ? s_flintId : id;
}
