using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockRedstoneRepeater : Block
    {

        public static readonly double[] RENDER_OFFSET = new double[] { -0.0625D, 1.0D / 16.0D, 0.1875D, 0.3125D };
        private static readonly int[] DELAY = new int[] { 1, 2, 3, 4 };
        private readonly bool lit;

        public BlockRedstoneRepeater(int id, bool lit) : base(id, 6, Material.PISTON_BREAKABLE)
        {
            this.lit = lit;
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return !world.shouldSuffocate(x, y - 1, z) ? false : base.canPlaceAt(world, x, y, z);
        }

        public override bool canGrow(World world, int x, int y, int z)
        {
            return !world.shouldSuffocate(x, y - 1, z) ? false : base.canGrow(world, x, y, z);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            int var6 = world.getBlockMeta(x, y, z);
            bool var7 = isPowered(world, x, y, z, var6);
            if (lit && !var7)
            {
                world.setBlockAndMetadataWithNotify(x, y, z, Block.REPEATER.id, var6);
            }
            else if (!lit)
            {
                world.setBlockAndMetadataWithNotify(x, y, z, Block.POWERED_REPEATER.id, var6);
                if (!var7)
                {
                    int var8 = (var6 & 12) >> 2;
                    world.scheduleBlockUpdate(x, y, z, Block.POWERED_REPEATER.id, DELAY[var8] * 2);
                }
            }

        }

        public override int getTexture(int side, int meta)
        {
            return side == 0 ? (lit ? 99 : 115) : (side == 1 ? (lit ? 147 : 131) : 5);
        }

        public override bool isSideVisible(BlockView blockView, int x, int y, int z, int side)
        {
            return side != 0 && side != 1;
        }

        public override int getRenderType()
        {
            return 15;
        }

        public override int getTexture(int side)
        {
            return getTexture(side, 0);
        }

        public override bool isStrongPoweringSide(World world, int x, int y, int z, int side)
        {
            return isPoweringSide(world, x, y, z, side);
        }

        public override bool isPoweringSide(BlockView blockView, int x, int y, int z, int side)
        {
            if (!lit)
            {
                return false;
            }
            else
            {
                int var6 = blockView.getBlockMeta(x, y, z) & 3;
                return var6 == 0 && side == 3 ? true : (var6 == 1 && side == 4 ? true : (var6 == 2 && side == 2 ? true : var6 == 3 && side == 5));
            }
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (!canGrow(world, x, y, z))
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }
            else
            {
                int var6 = world.getBlockMeta(x, y, z);
                bool var7 = isPowered(world, x, y, z, var6);
                int var8 = (var6 & 12) >> 2;
                if (lit && !var7)
                {
                    world.scheduleBlockUpdate(x, y, z, base.id, DELAY[var8] * 2);
                }
                else if (!lit && var7)
                {
                    world.scheduleBlockUpdate(x, y, z, base.id, DELAY[var8] * 2);
                }

            }
        }

        private bool isPowered(World world, int x, int y, int z, int meta)
        {
            int var6 = meta & 3;
            switch (var6)
            {
                case 0:
                    return world.isPoweringSide(x, y, z + 1, 3) || world.getBlockId(x, y, z + 1) == Block.REDSTONE_WIRE.id && world.getBlockMeta(x, y, z + 1) > 0;
                case 1:
                    return world.isPoweringSide(x - 1, y, z, 4) || world.getBlockId(x - 1, y, z) == Block.REDSTONE_WIRE.id && world.getBlockMeta(x - 1, y, z) > 0;
                case 2:
                    return world.isPoweringSide(x, y, z - 1, 2) || world.getBlockId(x, y, z - 1) == Block.REDSTONE_WIRE.id && world.getBlockMeta(x, y, z - 1) > 0;
                case 3:
                    return world.isPoweringSide(x + 1, y, z, 5) || world.getBlockId(x + 1, y, z) == Block.REDSTONE_WIRE.id && world.getBlockMeta(x + 1, y, z) > 0;
                default:
                    return false;
            }
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            int var6 = world.getBlockMeta(x, y, z);
            int var7 = (var6 & 12) >> 2;
            var7 = var7 + 1 << 2 & 12;
            world.setBlockMeta(x, y, z, var7 | var6 & 3);
            return true;
        }

        public override bool canEmitRedstonePower()
        {
            return false;
        }

        public override void onPlaced(World world, int x, int y, int z, EntityLiving placer)
        {
            int var6 = ((MathHelper.floor_double((double)(placer.rotationYaw * 4.0F / 360.0F) + 0.5D) & 3) + 2) % 4;
            world.setBlockMeta(x, y, z, var6);
            bool var7 = isPowered(world, x, y, z, var6);
            if (var7)
            {
                world.scheduleBlockUpdate(x, y, z, id, 1);
            }

        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            world.notifyNeighbors(x + 1, y, z, id);
            world.notifyNeighbors(x - 1, y, z, id);
            world.notifyNeighbors(x, y, z + 1, id);
            world.notifyNeighbors(x, y, z - 1, id);
            world.notifyNeighbors(x, y - 1, z, id);
            world.notifyNeighbors(x, y + 1, z, id);
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.redstoneRepeater.id;
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (lit)
            {
                int var6 = world.getBlockMeta(x, y, z);
                double var7 = (double)((float)x + 0.5F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var9 = (double)((float)y + 0.4F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var11 = (double)((float)z + 0.5F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var13 = 0.0D;
                double var15 = 0.0D;
                if (random.nextInt(2) == 0)
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
                            var15 = RENDER_OFFSET[var17];
                            break;
                        case 1:
                            var13 = -RENDER_OFFSET[var17];
                            break;
                        case 2:
                            var15 = -RENDER_OFFSET[var17];
                            break;
                        case 3:
                            var13 = RENDER_OFFSET[var17];
                            break;
                    }
                }

                world.addParticle("reddust", var7 + var13, var9, var11 + var15, 0.0D, 0.0D, 0.0D);
            }
        }
    }

}