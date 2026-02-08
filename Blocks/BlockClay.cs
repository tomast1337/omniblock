using betareborn.Blocks.Materials;
using betareborn.Items;

namespace betareborn.Blocks
{
    public class BlockClay : Block
    {

        public BlockClay(int id, int textureId) : base(id, textureId, Material.CLAY)
        {
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.clay.id;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 4;
        }
    }

}