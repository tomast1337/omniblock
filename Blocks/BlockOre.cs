using betareborn.Items;
using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockOre : Block
    {

        public BlockOre(int var1, int var2) : base(var1, var2, Material.STONE)
        {
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return blockID == Block.oreCoal.blockID ? Item.coal.id : (blockID == Block.oreDiamond.blockID ? Item.diamond.id : (blockID == Block.oreLapis.blockID ? Item.dyePowder.id : blockID));
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return blockID == Block.oreLapis.blockID ? 4 + var1.nextInt(5) : 1;
        }

        protected override int damageDropped(int var1)
        {
            return blockID == Block.oreLapis.blockID ? 4 : 0;
        }
    }

}