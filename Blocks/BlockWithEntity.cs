using betareborn.Worlds;
using betareborn.Blocks.BlockEntities;
using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public abstract class BlockWithEntity : Block
    {

        protected BlockWithEntity(int var1, Material var2) : base(var1, var2)
        {
            BLOCKS_WITH_ENTITY[var1] = true;
        }

        protected BlockWithEntity(int var1, int var2, Material var3) : base(var1, var2, var3)
        {
            BLOCKS_WITH_ENTITY[var1] = true;
        }

        public override void onPlaced(World var1, int var2, int var3, int var4)
        {
            base.onPlaced(var1, var2, var3, var4);
            var1.setBlockTileEntity(var2, var3, var4, getBlockEntity());
        }

        public override void onBreak(World var1, int var2, int var3, int var4)
        {
            base.onBreak(var1, var2, var3, var4);
            var1.removeBlockTileEntity(var2, var3, var4);
        }

        protected abstract BlockEntity getBlockEntity();
    }

}