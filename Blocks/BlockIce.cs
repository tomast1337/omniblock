using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockIce : BlockBreakable
    {

        public BlockIce(int var1, int var2) : base(var1, var2, Material.ICE, false)
        {
            slipperiness = 0.98F;
            setTickOnLoad(true);
        }

        public override int getRenderBlockPass()
        {
            return 1;
        }

        public override bool shouldSideBeRendered(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return base.shouldSideBeRendered(var1, var2, var3, var4, 1 - var5);
        }

        public override void harvestBlock(World var1, EntityPlayer var2, int var3, int var4, int var5, int var6)
        {
            base.harvestBlock(var1, var2, var3, var4, var5, var6);
            Material var7 = var1.getMaterial(var3, var4 - 1, var5);
            if (var7.blocksMovement() || var7.isFluid())
            {
                var1.setBlockWithNotify(var3, var4, var5, Block.waterMoving.blockID);
            }

        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 0;
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            if (var1.getSavedLightValue(EnumSkyBlock.Block, var2, var3, var4) > 11 - Block.lightOpacity[blockID])
            {
                dropBlockAsItem(var1, var2, var3, var4, var1.getBlockMetadata(var2, var3, var4));
                var1.setBlockWithNotify(var2, var3, var4, Block.waterStill.blockID);
            }

        }

        public override int getMobilityFlag()
        {
            return 0;
        }
    }

}