using betareborn.Entities;
using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockPistonMoving : BlockContainer
    {
        public BlockPistonMoving(int var1) : base(var1, Material.field_31067_B)
        {
            setHardness(-1.0F);
        }

        protected override TileEntity getBlockEntity()
        {
            return null;
        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
        }

        public override void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
            TileEntity var5 = var1.getBlockTileEntity(var2, var3, var4);
            if (var5 != null && var5 is TileEntityPiston)
            {
                ((TileEntityPiston)var5).func_31011_l();
            }
            else
            {
                base.onBlockRemoval(var1, var2, var3, var4);
            }

        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return false;
        }

        public override bool canPlaceBlockOnSide(World var1, int var2, int var3, int var4, int var5)
        {
            return false;
        }

        public override int getRenderType()
        {
            return -1;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override bool blockActivated(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            if (!var1.multiplayerWorld && var1.getBlockTileEntity(var2, var3, var4) == null)
            {
                var1.setBlockWithNotify(var2, var3, var4, 0);
                return true;
            }
            else
            {
                return false;
            }
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return 0;
        }

        public override void dropBlockAsItemWithChance(World var1, int var2, int var3, int var4, int var5, float var6)
        {
            if (!var1.multiplayerWorld)
            {
                TileEntityPiston var7 = func_31034_c(var1, var2, var3, var4);
                if (var7 != null)
                {
                    Block.blocksList[var7.getStoredBlockID()].dropBlockAsItem(var1, var2, var3, var4, var7.getPushedBlockData());
                }
            }
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            if (!var1.multiplayerWorld && var1.getBlockTileEntity(var2, var3, var4) == null)
            {
            }

        }

        public static TileEntity func_31036_a(int var0, int var1, int var2, bool var3, bool var4)
        {
            return new TileEntityPiston(var0, var1, var2, var3, var4);
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            TileEntityPiston var5 = func_31034_c(var1, var2, var3, var4);
            if (var5 == null)
            {
                return null;
            }
            else
            {
                float var6 = var5.func_31008_a(0.0F);
                if (var5.func_31015_b())
                {
                    var6 = 1.0F - var6;
                }

                return func_31035_a(var1, var2, var3, var4, var5.getStoredBlockID(), var6, var5.func_31009_d());
            }
        }

        public override void setBlockBoundsBasedOnState(IBlockAccess var1, int var2, int var3, int var4)
        {
            TileEntityPiston var5 = func_31034_c(var1, var2, var3, var4);
            if (var5 != null)
            {
                Block var6 = Block.blocksList[var5.getStoredBlockID()];
                if (var6 == null || var6 == this)
                {
                    return;
                }

                var6.setBlockBoundsBasedOnState(var1, var2, var3, var4);
                float var7 = var5.func_31008_a(0.0F);
                if (var5.func_31015_b())
                {
                    var7 = 1.0F - var7;
                }

                int var8 = var5.func_31009_d();
                minX = var6.minX - (double)((float)PistonBlockTextures.field_31056_b[var8] * var7);
                minY = var6.minY - (double)((float)PistonBlockTextures.field_31059_c[var8] * var7);
                minZ = var6.minZ - (double)((float)PistonBlockTextures.field_31058_d[var8] * var7);
                maxX = var6.maxX - (double)((float)PistonBlockTextures.field_31056_b[var8] * var7);
                maxY = var6.maxY - (double)((float)PistonBlockTextures.field_31059_c[var8] * var7);
                maxZ = var6.maxZ - (double)((float)PistonBlockTextures.field_31058_d[var8] * var7);
            }

        }

        public Box func_31035_a(World var1, int var2, int var3, int var4, int var5, float var6, int var7)
        {
            if (var5 != 0 && var5 != blockID)
            {
                Box var8 = Block.blocksList[var5].getCollisionBoundingBoxFromPool(var1, var2, var3, var4);
                if (var8 == null)
                {
                    return null;
                }
                else
                {
                    var8.minX -= (double)((float)PistonBlockTextures.field_31056_b[var7] * var6);
                    var8.maxX -= (double)((float)PistonBlockTextures.field_31056_b[var7] * var6);
                    var8.minY -= (double)((float)PistonBlockTextures.field_31059_c[var7] * var6);
                    var8.maxY -= (double)((float)PistonBlockTextures.field_31059_c[var7] * var6);
                    var8.minZ -= (double)((float)PistonBlockTextures.field_31058_d[var7] * var6);
                    var8.maxZ -= (double)((float)PistonBlockTextures.field_31058_d[var7] * var6);
                    return var8;
                }
            }
            else
            {
                return null;
            }
        }

        private TileEntityPiston func_31034_c(IBlockAccess var1, int var2, int var3, int var4)
        {
            TileEntity var5 = var1.getBlockTileEntity(var2, var3, var4);
            return var5 != null && var5 is TileEntityPiston ? (TileEntityPiston)var5 : null;
        }
    }

}