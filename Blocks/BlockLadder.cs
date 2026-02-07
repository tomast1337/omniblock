using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLadder : Block
    {

        public BlockLadder(int var1, int var2) : base(var1, var2, Material.circuits)
        {
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            int var5 = var1.getBlockMetadata(var2, var3, var4);
            float var6 = 2.0F / 16.0F;
            if (var5 == 2)
            {
                setBlockBounds(0.0F, 0.0F, 1.0F - var6, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 3)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var6);
            }

            if (var5 == 4)
            {
                setBlockBounds(1.0F - var6, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 5)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, var6, 1.0F, 1.0F);
            }

            return base.getCollisionBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override Box getSelectedBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            int var5 = var1.getBlockMetadata(var2, var3, var4);
            float var6 = 2.0F / 16.0F;
            if (var5 == 2)
            {
                setBlockBounds(0.0F, 0.0F, 1.0F - var6, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 3)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var6);
            }

            if (var5 == 4)
            {
                setBlockBounds(1.0F - var6, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 5)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, var6, 1.0F, 1.0F);
            }

            return base.getSelectedBoundingBoxFromPool(var1, var2, var3, var4);
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
            return 8;
        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return var1.isBlockNormalCube(var2 - 1, var3, var4) ? true : (var1.isBlockNormalCube(var2 + 1, var3, var4) ? true : (var1.isBlockNormalCube(var2, var3, var4 - 1) ? true : var1.isBlockNormalCube(var2, var3, var4 + 1)));
        }

        public override void onBlockPlaced(World var1, int var2, int var3, int var4, int var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            if ((var6 == 0 || var5 == 2) && var1.isBlockNormalCube(var2, var3, var4 + 1))
            {
                var6 = 2;
            }

            if ((var6 == 0 || var5 == 3) && var1.isBlockNormalCube(var2, var3, var4 - 1))
            {
                var6 = 3;
            }

            if ((var6 == 0 || var5 == 4) && var1.isBlockNormalCube(var2 + 1, var3, var4))
            {
                var6 = 4;
            }

            if ((var6 == 0 || var5 == 5) && var1.isBlockNormalCube(var2 - 1, var3, var4))
            {
                var6 = 5;
            }

            var1.setBlockMetadataWithNotify(var2, var3, var4, var6);
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            bool var7 = false;
            if (var6 == 2 && var1.isBlockNormalCube(var2, var3, var4 + 1))
            {
                var7 = true;
            }

            if (var6 == 3 && var1.isBlockNormalCube(var2, var3, var4 - 1))
            {
                var7 = true;
            }

            if (var6 == 4 && var1.isBlockNormalCube(var2 + 1, var3, var4))
            {
                var7 = true;
            }

            if (var6 == 5 && var1.isBlockNormalCube(var2 - 1, var3, var4))
            {
                var7 = true;
            }

            if (!var7)
            {
                dropBlockAsItem(var1, var2, var3, var4, var6);
                var1.setBlockWithNotify(var2, var3, var4, 0);
            }

            base.onNeighborBlockChange(var1, var2, var3, var4, var5);
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 1;
        }
    }

}