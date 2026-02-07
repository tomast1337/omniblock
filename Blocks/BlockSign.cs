using betareborn.Items;
using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Blocks
{
    public class BlockSign : BlockContainer
    {

        private Class signEntityClass;
        private bool isFreestanding;

        public BlockSign(int var1, Class var2, bool var3) : base(var1, Material.wood)
        {
            isFreestanding = var3;
            blockIndexInTexture = 4;
            signEntityClass = var2;
            float var4 = 0.25F;
            float var5 = 1.0F;
            setBlockBounds(0.5F - var4, 0.0F, 0.5F - var4, 0.5F + var4, var5, 0.5F + var4);
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return null;
        }

        public override Box getSelectedBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            setBlockBoundsBasedOnState(var1, var2, var3, var4);
            return base.getSelectedBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override void setBlockBoundsBasedOnState(IBlockAccess var1, int var2, int var3, int var4)
        {
            if (!isFreestanding)
            {
                int var5 = var1.getBlockMetadata(var2, var3, var4);
                float var6 = 9.0F / 32.0F;
                float var7 = 25.0F / 32.0F;
                float var8 = 0.0F;
                float var9 = 1.0F;
                float var10 = 2.0F / 16.0F;
                setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                if (var5 == 2)
                {
                    setBlockBounds(var8, var6, 1.0F - var10, var9, var7, 1.0F);
                }

                if (var5 == 3)
                {
                    setBlockBounds(var8, var6, 0.0F, var9, var7, var10);
                }

                if (var5 == 4)
                {
                    setBlockBounds(1.0F - var10, var6, var8, 1.0F, var7, var9);
                }

                if (var5 == 5)
                {
                    setBlockBounds(0.0F, var6, var8, var10, var7, var9);
                }

            }
        }

        public override int getRenderType()
        {
            return -1;
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        protected override TileEntity getBlockEntity()
        {
            try
            {
                return (TileEntity)signEntityClass.newInstance();
            }
            catch (java.lang.Exception var2)
            {
                throw new RuntimeException(var2);
            }
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return Item.sign.shiftedIndex;
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            bool var6 = false;
            if (isFreestanding)
            {
                if (!var1.getBlockMaterial(var2, var3 - 1, var4).isSolid())
                {
                    var6 = true;
                }
            }
            else
            {
                int var7 = var1.getBlockMetadata(var2, var3, var4);
                var6 = true;
                if (var7 == 2 && var1.getBlockMaterial(var2, var3, var4 + 1).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 3 && var1.getBlockMaterial(var2, var3, var4 - 1).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 4 && var1.getBlockMaterial(var2 + 1, var3, var4).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 5 && var1.getBlockMaterial(var2 - 1, var3, var4).isSolid())
                {
                    var6 = false;
                }
            }

            if (var6)
            {
                dropBlockAsItem(var1, var2, var3, var4, var1.getBlockMetadata(var2, var3, var4));
                var1.setBlockWithNotify(var2, var3, var4, 0);
            }

            base.onNeighborBlockChange(var1, var2, var3, var4, var5);
        }
    }

}