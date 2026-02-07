using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockStationary : BlockFluid
    {
        public BlockStationary(int var1, Material var2) : base(var1, var2)
        {
            setTickOnLoad(false);
            if (var2 == Material.LAVA)
            {
                setTickOnLoad(true);
            }

        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            base.onNeighborBlockChange(var1, var2, var3, var4, var5);
            if (var1.getBlockId(var2, var3, var4) == blockID)
            {
                func_30004_j(var1, var2, var3, var4);
            }

        }

        private void func_30004_j(World var1, int var2, int var3, int var4)
        {
            int var5 = var1.getBlockMetadata(var2, var3, var4);
            var1.editingBlocks = true;
            var1.setBlockAndMetadata(var2, var3, var4, blockID - 1, var5);
            var1.markBlocksDirty(var2, var3, var4, var2, var3, var4);
            var1.scheduleBlockUpdate(var2, var3, var4, blockID - 1, tickRate());
            var1.editingBlocks = false;
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            if (blockMaterial == Material.LAVA)
            {
                int var6 = var5.nextInt(3);

                for (int var7 = 0; var7 < var6; ++var7)
                {
                    var2 += var5.nextInt(3) - 1;
                    ++var3;
                    var4 += var5.nextInt(3) - 1;
                    int var8 = var1.getBlockId(var2, var3, var4);
                    if (var8 == 0)
                    {
                        if (func_301_k(var1, var2 - 1, var3, var4) || func_301_k(var1, var2 + 1, var3, var4) || func_301_k(var1, var2, var3, var4 - 1) || func_301_k(var1, var2, var3, var4 + 1) || func_301_k(var1, var2, var3 - 1, var4) || func_301_k(var1, var2, var3 + 1, var4))
                        {
                            var1.setBlockWithNotify(var2, var3, var4, Block.fire.blockID);
                            return;
                        }
                    }
                    else if (Block.blocksList[var8].blockMaterial.blocksMovement())
                    {
                        return;
                    }
                }
            }

        }

        private bool func_301_k(World var1, int var2, int var3, int var4)
        {
            return var1.getMaterial(var2, var3, var4).isBurnable();
        }
    }

}