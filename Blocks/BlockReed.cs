using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockReed : Block
    {

        public BlockReed(int var1, int var2) : base(var1, Material.plants)
        {
            blockIndexInTexture = var2;
            float var3 = 6.0F / 16.0F;
            setBlockBounds(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, 1.0F, 0.5F + var3);
            setTickOnLoad(true);
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            if (var1.isAirBlock(var2, var3 + 1, var4))
            {
                int var6;
                for (var6 = 1; var1.getBlockId(var2, var3 - var6, var4) == blockID; ++var6)
                {
                }

                if (var6 < 3)
                {
                    int var7 = var1.getBlockMetadata(var2, var3, var4);
                    if (var7 == 15)
                    {
                        var1.setBlockWithNotify(var2, var3 + 1, var4, blockID);
                        var1.setBlockMetadataWithNotify(var2, var3, var4, 0);
                    }
                    else
                    {
                        var1.setBlockMetadataWithNotify(var2, var3, var4, var7 + 1);
                    }
                }
            }

        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            int var5 = var1.getBlockId(var2, var3 - 1, var4);
            return var5 == blockID ? true : (var5 != Block.grass.blockID && var5 != Block.dirt.blockID ? false : (var1.getBlockMaterial(var2 - 1, var3 - 1, var4) == Material.water ? true : (var1.getBlockMaterial(var2 + 1, var3 - 1, var4) == Material.water ? true : (var1.getBlockMaterial(var2, var3 - 1, var4 - 1) == Material.water ? true : var1.getBlockMaterial(var2, var3 - 1, var4 + 1) == Material.water))));
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            checkBlockCoordValid(var1, var2, var3, var4);
        }

        protected void checkBlockCoordValid(World var1, int var2, int var3, int var4)
        {
            if (!canBlockStay(var1, var2, var3, var4))
            {
                dropBlockAsItem(var1, var2, var3, var4, var1.getBlockMetadata(var2, var3, var4));
                var1.setBlockWithNotify(var2, var3, var4, 0);
            }

        }

        public override bool canBlockStay(World var1, int var2, int var3, int var4)
        {
            return canPlaceBlockAt(var1, var2, var3, var4);
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return null;
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return Item.reed.shiftedIndex;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 1;
        }
    }

}