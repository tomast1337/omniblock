using BetaSharp.Blocks.Materials;
using BetaSharp.Items;

namespace BetaSharp.Blocks;

internal class BlockClay(int id, int textureId) : Block(id, textureId, Material.Clay)
{
    private static readonly int s_clayId = Item.ByName("clay").Id;

    public override int getDroppedItemId(int blockMeta) => s_clayId;

    public override int getDroppedItemCount() => 4;
}
