using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockButton : Block
    {
        public BlockButton(int id, int textureId) : base(id, textureId, Material.PISTON_BREAKABLE)
        {
            setTickRandomly(true);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override int getTickRate()
        {
            return 20;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool canPlaceAt(World world, int x, int y, int z, int side)
        {
            return side == 2 && world.shouldSuffocate(x, y, z + 1) ? true : (side == 3 && world.shouldSuffocate(x, y, z - 1) ? true : (side == 4 && world.shouldSuffocate(x + 1, y, z) ? true : side == 5 && world.shouldSuffocate(x - 1, y, z)));
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x - 1, y, z) ? true : (world.shouldSuffocate(x + 1, y, z) ? true : (world.shouldSuffocate(x, y, z - 1) ? true : world.shouldSuffocate(x, y, z + 1)));
        }

        public override void onPlaced(World world, int x, int y, int z, int direction)
        {
            int var6 = world.getBlockMeta(x, y, z);
            int var7 = var6 & 8;
            var6 &= 7;
            if (direction == 2 && world.shouldSuffocate(x, y, z + 1))
            {
                var6 = 4;
            }
            else if (direction == 3 && world.shouldSuffocate(x, y, z - 1))
            {
                var6 = 3;
            }
            else if (direction == 4 && world.shouldSuffocate(x + 1, y, z))
            {
                var6 = 2;
            }
            else if (direction == 5 && world.shouldSuffocate(x - 1, y, z))
            {
                var6 = 1;
            }
            else
            {
                var6 = getPlacementSide(world, x, y, z);
            }

            world.setBlockMeta(x, y, z, var6 + var7);
        }

        private int getPlacementSide(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x - 1, y, z) ? 1 : (world.shouldSuffocate(x + 1, y, z) ? 2 : (world.shouldSuffocate(x, y, z - 1) ? 3 : (world.shouldSuffocate(x, y, z + 1) ? 4 : 1)));
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (breakIfCannotPlaceAt(world, x, y, z))
            {
                int var6 = world.getBlockMeta(x, y, z) & 7;
                bool var7 = false;
                if (!world.shouldSuffocate(x - 1, y, z) && var6 == 1)
                {
                    var7 = true;
                }

                if (!world.shouldSuffocate(x + 1, y, z) && var6 == 2)
                {
                    var7 = true;
                }

                if (!world.shouldSuffocate(x, y, z - 1) && var6 == 3)
                {
                    var7 = true;
                }

                if (!world.shouldSuffocate(x, y, z + 1) && var6 == 4)
                {
                    var7 = true;
                }

                if (var7)
                {
                    dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                    world.setBlockWithNotify(x, y, z, 0);
                }
            }

        }

        private bool breakIfCannotPlaceAt(World world, int x, int y, int z)
        {
            if (!canPlaceAt(world, x, y, z))
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
                return false;
            }
            else
            {
                return true;
            }
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            int var5 = blockView.getBlockMeta(x, y, z);
            int var6 = var5 & 7;
            bool var7 = (var5 & 8) > 0;
            float var8 = 6.0F / 16.0F;
            float var9 = 10.0F / 16.0F;
            float var10 = 3.0F / 16.0F;
            float var11 = 2.0F / 16.0F;
            if (var7)
            {
                var11 = 1.0F / 16.0F;
            }

            if (var6 == 1)
            {
                setBoundingBox(0.0F, var8, 0.5F - var10, var11, var9, 0.5F + var10);
            }
            else if (var6 == 2)
            {
                setBoundingBox(1.0F - var11, var8, 0.5F - var10, 1.0F, var9, 0.5F + var10);
            }
            else if (var6 == 3)
            {
                setBoundingBox(0.5F - var10, var8, 0.0F, 0.5F + var10, var9, var11);
            }
            else if (var6 == 4)
            {
                setBoundingBox(0.5F - var10, var8, 1.0F - var11, 0.5F + var10, var9, 1.0F);
            }

        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer player)
        {
            onUse(world, x, y, z, player);
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            int var6 = world.getBlockMeta(x, y, z);
            int var7 = var6 & 7;
            int var8 = 8 - (var6 & 8);
            if (var8 == 0)
            {
                return true;
            }
            else
            {
                world.setBlockMeta(x, y, z, var7 + var8);
                world.setBlocksDirty(x, y, z, x, y, z);
                world.playSound((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, "random.click", 0.3F, 0.6F);
                world.notifyNeighbors(x, y, z, id);
                if (var7 == 1)
                {
                    world.notifyNeighbors(x - 1, y, z, id);
                }
                else if (var7 == 2)
                {
                    world.notifyNeighbors(x + 1, y, z, id);
                }
                else if (var7 == 3)
                {
                    world.notifyNeighbors(x, y, z - 1, id);
                }
                else if (var7 == 4)
                {
                    world.notifyNeighbors(x, y, z + 1, id);
                }
                else
                {
                    world.notifyNeighbors(x, y - 1, z, id);
                }

                world.scheduleBlockUpdate(x, y, z, id, getTickRate());
                return true;
            }
        }

        public override void onBreak(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            if ((var5 & 8) > 0)
            {
                world.notifyNeighbors(x, y, z, id);
                int var6 = var5 & 7;
                if (var6 == 1)
                {
                    world.notifyNeighbors(x - 1, y, z, id);
                }
                else if (var6 == 2)
                {
                    world.notifyNeighbors(x + 1, y, z, id);
                }
                else if (var6 == 3)
                {
                    world.notifyNeighbors(x, y, z - 1, id);
                }
                else if (var6 == 4)
                {
                    world.notifyNeighbors(x, y, z + 1, id);
                }
                else
                {
                    world.notifyNeighbors(x, y - 1, z, id);
                }
            }

            base.onBreak(world, x, y, z);
        }

        public override bool isPoweringSide(BlockView blockView, int x, int y, int z, int side)
        {
            return (blockView.getBlockMeta(x, y, z) & 8) > 0;
        }

        public override bool isStrongPoweringSide(World world, int x, int y, int z, int side)
        {
            int var6 = world.getBlockMeta(x, y, z);
            if ((var6 & 8) == 0)
            {
                return false;
            }
            else
            {
                int var7 = var6 & 7;
                return var7 == 5 && side == 1 ? true : (var7 == 4 && side == 2 ? true : (var7 == 3 && side == 3 ? true : (var7 == 2 && side == 4 ? true : var7 == 1 && side == 5)));
            }
        }

        public override bool canEmitRedstonePower()
        {
            return true;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if ((var6 & 8) != 0)
                {
                    world.setBlockMeta(x, y, z, var6 & 7);
                    world.notifyNeighbors(x, y, z, id);
                    int var7 = var6 & 7;
                    if (var7 == 1)
                    {
                        world.notifyNeighbors(x - 1, y, z, id);
                    }
                    else if (var7 == 2)
                    {
                        world.notifyNeighbors(x + 1, y, z, id);
                    }
                    else if (var7 == 3)
                    {
                        world.notifyNeighbors(x, y, z - 1, id);
                    }
                    else if (var7 == 4)
                    {
                        world.notifyNeighbors(x, y, z + 1, id);
                    }
                    else
                    {
                        world.notifyNeighbors(x, y - 1, z, id);
                    }

                    world.playSound((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, "random.click", 0.3F, 0.5F);
                    world.setBlocksDirty(x, y, z, x, y, z);
                }
            }
        }

        public override void setupRenderBoundingBox()
        {
            float var1 = 3.0F / 16.0F;
            float var2 = 2.0F / 16.0F;
            float var3 = 2.0F / 16.0F;
            setBoundingBox(0.5F - var1, 0.5F - var2, 0.5F - var3, 0.5F + var1, 0.5F + var2, 0.5F + var3);
        }
    }

}