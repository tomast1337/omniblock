using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockWeb : Block
    {
        public BlockWeb(int var1, int var2) : base(var1, var2, Material.field_31068_A)
        {
        }

        public override void onEntityCollidedWithBlock(World var1, int var2, int var3, int var4, Entity var5)
        {
            var5.isInWeb = true;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return null;
        }

        public override int getRenderType()
        {
            return 1;
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return Item.silk.shiftedIndex;
        }
    }

}