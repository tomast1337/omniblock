using betareborn.Blocks.Materials;
using betareborn.Items;

namespace betareborn.Blocks
{
    public class BlockOre : Block
    {

        public BlockOre(int id, int textureId) : base(id, textureId, Material.STONE)
        {
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return id == Block.COAL_ORE.id ? Item.coal.id : (id == Block.DIAMOND_ORE.id ? Item.diamond.id : (id == Block.LAPIS_ORE.id ? Item.dyePowder.id : id));
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return id == Block.LAPIS_ORE.id ? 4 + random.nextInt(5) : 1;
        }

        protected override int getDroppedItemMeta(int blockMeta)
        {
            return id == Block.LAPIS_ORE.id ? 4 : 0;
        }
    }

}