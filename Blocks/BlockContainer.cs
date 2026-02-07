using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public abstract class BlockContainer : Block
    {

        protected BlockContainer(int var1, Material var2) : base(var1, var2)
        {
            BLOCKS_WITH_ENTITY[var1] = true;
        }

        protected BlockContainer(int var1, int var2, Material var3) : base(var1, var2, var3)
        {
            BLOCKS_WITH_ENTITY[var1] = true;
        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
            base.onBlockAdded(var1, var2, var3, var4);
            var1.setBlockTileEntity(var2, var3, var4, getBlockEntity());
        }

        public override void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
            base.onBlockRemoval(var1, var2, var3, var4);
            var1.removeBlockTileEntity(var2, var3, var4);
        }

        protected abstract TileEntity getBlockEntity();
    }

}