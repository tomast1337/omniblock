using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockRedstoneRepeater : Block
    {

        public static readonly double[] field_22024_a = new double[] { -0.0625D, 1.0D / 16.0D, 0.1875D, 0.3125D };
        private static readonly int[] field_22023_b = new int[] { 1, 2, 3, 4 };
        private readonly bool isRepeaterPowered;

        public BlockRedstoneRepeater(int var1, bool var2) : base(var1, 6, Material.circuits)
        {
            isRepeaterPowered = var2;
            setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return !var1.isBlockNormalCube(var2, var3 - 1, var4) ? false : base.canPlaceBlockAt(var1, var2, var3, var4);
        }

        public override bool canBlockStay(World var1, int var2, int var3, int var4)
        {
            return !var1.isBlockNormalCube(var2, var3 - 1, var4) ? false : base.canBlockStay(var1, var2, var3, var4);
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            bool var7 = func_22022_g(var1, var2, var3, var4, var6);
            if (isRepeaterPowered && !var7)
            {
                var1.setBlockAndMetadataWithNotify(var2, var3, var4, Block.redstoneRepeaterIdle.blockID, var6);
            }
            else if (!isRepeaterPowered)
            {
                var1.setBlockAndMetadataWithNotify(var2, var3, var4, Block.redstoneRepeaterActive.blockID, var6);
                if (!var7)
                {
                    int var8 = (var6 & 12) >> 2;
                    var1.scheduleBlockUpdate(var2, var3, var4, Block.redstoneRepeaterActive.blockID, field_22023_b[var8] * 2);
                }
            }

        }

        public override int getBlockTextureFromSideAndMetadata(int var1, int var2)
        {
            return var1 == 0 ? (isRepeaterPowered ? 99 : 115) : (var1 == 1 ? (isRepeaterPowered ? 147 : 131) : 5);
        }

        public override bool shouldSideBeRendered(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            return var5 != 0 && var5 != 1;
        }

        public override int getRenderType()
        {
            return 15;
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return getBlockTextureFromSideAndMetadata(var1, 0);
        }

        public override bool isIndirectlyPoweringTo(World var1, int var2, int var3, int var4, int var5)
        {
            return isPoweringTo(var1, var2, var3, var4, var5);
        }

        public override bool isPoweringTo(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            if (!isRepeaterPowered)
            {
                return false;
            }
            else
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4) & 3;
                return var6 == 0 && var5 == 3 ? true : (var6 == 1 && var5 == 4 ? true : (var6 == 2 && var5 == 2 ? true : var6 == 3 && var5 == 5));
            }
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            if (!canBlockStay(var1, var2, var3, var4))
            {
                dropBlockAsItem(var1, var2, var3, var4, var1.getBlockMetadata(var2, var3, var4));
                var1.setBlockWithNotify(var2, var3, var4, 0);
            }
            else
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4);
                bool var7 = func_22022_g(var1, var2, var3, var4, var6);
                int var8 = (var6 & 12) >> 2;
                if (isRepeaterPowered && !var7)
                {
                    var1.scheduleBlockUpdate(var2, var3, var4, blockID, field_22023_b[var8] * 2);
                }
                else if (!isRepeaterPowered && var7)
                {
                    var1.scheduleBlockUpdate(var2, var3, var4, blockID, field_22023_b[var8] * 2);
                }

            }
        }

        private bool func_22022_g(World var1, int var2, int var3, int var4, int var5)
        {
            int var6 = var5 & 3;
            switch (var6)
            {
                case 0:
                    return var1.isBlockIndirectlyProvidingPowerTo(var2, var3, var4 + 1, 3) || var1.getBlockId(var2, var3, var4 + 1) == Block.redstoneWire.blockID && var1.getBlockMetadata(var2, var3, var4 + 1) > 0;
                case 1:
                    return var1.isBlockIndirectlyProvidingPowerTo(var2 - 1, var3, var4, 4) || var1.getBlockId(var2 - 1, var3, var4) == Block.redstoneWire.blockID && var1.getBlockMetadata(var2 - 1, var3, var4) > 0;
                case 2:
                    return var1.isBlockIndirectlyProvidingPowerTo(var2, var3, var4 - 1, 2) || var1.getBlockId(var2, var3, var4 - 1) == Block.redstoneWire.blockID && var1.getBlockMetadata(var2, var3, var4 - 1) > 0;
                case 3:
                    return var1.isBlockIndirectlyProvidingPowerTo(var2 + 1, var3, var4, 5) || var1.getBlockId(var2 + 1, var3, var4) == Block.redstoneWire.blockID && var1.getBlockMetadata(var2 + 1, var3, var4) > 0;
                default:
                    return false;
            }
        }

        public override bool blockActivated(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            int var7 = (var6 & 12) >> 2;
            var7 = var7 + 1 << 2 & 12;
            var1.setBlockMetadataWithNotify(var2, var3, var4, var7 | var6 & 3);
            return true;
        }

        public override bool canProvidePower()
        {
            return false;
        }

        public override void onBlockPlacedBy(World var1, int var2, int var3, int var4, EntityLiving var5)
        {
            int var6 = ((MathHelper.floor_double((double)(var5.rotationYaw * 4.0F / 360.0F) + 0.5D) & 3) + 2) % 4;
            var1.setBlockMetadataWithNotify(var2, var3, var4, var6);
            bool var7 = func_22022_g(var1, var2, var3, var4, var6);
            if (var7)
            {
                var1.scheduleBlockUpdate(var2, var3, var4, blockID, 1);
            }

        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
            var1.notifyBlocksOfNeighborChange(var2 + 1, var3, var4, blockID);
            var1.notifyBlocksOfNeighborChange(var2 - 1, var3, var4, blockID);
            var1.notifyBlocksOfNeighborChange(var2, var3, var4 + 1, blockID);
            var1.notifyBlocksOfNeighborChange(var2, var3, var4 - 1, blockID);
            var1.notifyBlocksOfNeighborChange(var2, var3 - 1, var4, blockID);
            var1.notifyBlocksOfNeighborChange(var2, var3 + 1, var4, blockID);
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return Item.redstoneRepeater.shiftedIndex;
        }

        public override void randomDisplayTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            if (isRepeaterPowered)
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4);
                double var7 = (double)((float)var2 + 0.5F) + (double)(var5.nextFloat() - 0.5F) * 0.2D;
                double var9 = (double)((float)var3 + 0.4F) + (double)(var5.nextFloat() - 0.5F) * 0.2D;
                double var11 = (double)((float)var4 + 0.5F) + (double)(var5.nextFloat() - 0.5F) * 0.2D;
                double var13 = 0.0D;
                double var15 = 0.0D;
                if (var5.nextInt(2) == 0)
                {
                    switch (var6 & 3)
                    {
                        case 0:
                            var15 = -0.3125D;
                            break;
                        case 1:
                            var13 = 0.3125D;
                            break;
                        case 2:
                            var15 = 0.3125D;
                            break;
                        case 3:
                            var13 = -0.3125D;
                            break;
                    }
                }
                else
                {
                    int var17 = (var6 & 12) >> 2;
                    switch (var6 & 3)
                    {
                        case 0:
                            var15 = field_22024_a[var17];
                            break;
                        case 1:
                            var13 = -field_22024_a[var17];
                            break;
                        case 2:
                            var15 = -field_22024_a[var17];
                            break;
                        case 3:
                            var13 = field_22024_a[var17];
                            break;
                    }
                }

                var1.addParticle("reddust", var7 + var13, var9, var11 + var15, 0.0D, 0.0D, 0.0D);
            }
        }
    }

}