using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockFlowing : BlockFluid
    {
        int adjacentSources = 0;
        bool[] spread = new bool[4];
        int[] distanceToGap = new int[4];

        public BlockFlowing(int id, Material material) : base(id, material)
        {
        }

        private void convertToSource(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            world.setBlockAndMetadata(x, y, z, id + 1, var5);
            world.setBlocksDirty(x, y, z, x, y, z);
            world.markBlockNeedsUpdate(x, y, z);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            int var6 = getLiquidState(world, x, y, z);
            sbyte var7 = 1;
            if (material == Material.LAVA && !world.dimension.isHellWorld)
            {
                var7 = 2;
            }

            bool var8 = true;
            int var10;
            if (var6 > 0)
            {
                int var9 = -100;
                adjacentSources = 0;
                int var12 = getLowestDepth(world, x - 1, y, z, var9);
                var12 = getLowestDepth(world, x + 1, y, z, var12);
                var12 = getLowestDepth(world, x, y, z - 1, var12);
                var12 = getLowestDepth(world, x, y, z + 1, var12);
                var10 = var12 + var7;
                if (var10 >= 8 || var12 < 0)
                {
                    var10 = -1;
                }

                if (getLiquidState(world, x, y + 1, z) >= 0)
                {
                    int var11 = getLiquidState(world, x, y + 1, z);
                    if (var11 >= 8)
                    {
                        var10 = var11;
                    }
                    else
                    {
                        var10 = var11 + 8;
                    }
                }

                if (adjacentSources >= 2 && material == Material.WATER)
                {
                    if (world.getMaterial(x, y - 1, z).isSolid())
                    {
                        var10 = 0;
                    }
                    else if (world.getMaterial(x, y - 1, z) == material && world.getBlockMeta(x, y, z) == 0)
                    {
                        var10 = 0;
                    }
                }

                if (material == Material.LAVA && var6 < 8 && var10 < 8 && var10 > var6 && random.nextInt(4) != 0)
                {
                    var10 = var6;
                    var8 = false;
                }

                if (var10 != var6)
                {
                    var6 = var10;
                    if (var10 < 0)
                    {
                        world.setBlockWithNotify(x, y, z, 0);
                    }
                    else
                    {
                        world.setBlockMeta(x, y, z, var10);
                        world.scheduleBlockUpdate(x, y, z, id, getTickRate());
                        world.notifyNeighbors(x, y, z, id);
                    }
                }
                else if (var8)
                {
                    convertToSource(world, x, y, z);
                }
            }
            else
            {
                convertToSource(world, x, y, z);
            }

            if (canSpreadTo(world, x, y - 1, z))
            {
                if (var6 >= 8)
                {
                    world.setBlockAndMetadataWithNotify(x, y - 1, z, id, var6);
                }
                else
                {
                    world.setBlockAndMetadataWithNotify(x, y - 1, z, id, var6 + 8);
                }
            }
            else if (var6 >= 0 && (var6 == 0 || isLiquidBreaking(world, x, y - 1, z)))
            {
                bool[] var13 = getSpread(world, x, y, z);
                var10 = var6 + var7;
                if (var6 >= 8)
                {
                    var10 = 1;
                }

                if (var10 >= 8)
                {
                    return;
                }

                if (var13[0])
                {
                    spreadTo(world, x - 1, y, z, var10);
                }

                if (var13[1])
                {
                    spreadTo(world, x + 1, y, z, var10);
                }

                if (var13[2])
                {
                    spreadTo(world, x, y, z - 1, var10);
                }

                if (var13[3])
                {
                    spreadTo(world, x, y, z + 1, var10);
                }
            }

        }

        private void spreadTo(World world, int x, int y, int z, int depth)
        {
            if (canSpreadTo(world, x, y, z))
            {
                int var6 = world.getBlockId(x, y, z);
                if (var6 > 0)
                {
                    if (material == Material.LAVA)
                    {
                        fizz(world, x, y, z);
                    }
                    else
                    {
                        Block.BLOCKS[var6].dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                    }
                }

                world.setBlockAndMetadataWithNotify(x, y, z, id, depth);
            }

        }

        private int getDistanceToGap(World world, int x, int y, int z, int distance, int fromDirection)
        {
            int var7 = 1000;

            for (int var8 = 0; var8 < 4; ++var8)
            {
                if ((var8 != 0 || fromDirection != 1) && (var8 != 1 || fromDirection != 0) && (var8 != 2 || fromDirection != 3) && (var8 != 3 || fromDirection != 2))
                {
                    int var9 = x;
                    int var11 = z;
                    if (var8 == 0)
                    {
                        var9 = x - 1;
                    }

                    if (var8 == 1)
                    {
                        ++var9;
                    }

                    if (var8 == 2)
                    {
                        var11 = z - 1;
                    }

                    if (var8 == 3)
                    {
                        ++var11;
                    }

                    if (!isLiquidBreaking(world, var9, y, var11) && (world.getMaterial(var9, y, var11) != material || world.getBlockMeta(var9, y, var11) != 0))
                    {
                        if (!isLiquidBreaking(world, var9, y - 1, var11))
                        {
                            return distance;
                        }

                        if (distance < 4)
                        {
                            int var12 = getDistanceToGap(world, var9, y, var11, distance + 1, var8);
                            if (var12 < var7)
                            {
                                var7 = var12;
                            }
                        }
                    }
                }
            }

            return var7;
        }

        private bool[] getSpread(World world, int x, int y, int z)
        {
            int var5;
            int var6;
            for (var5 = 0; var5 < 4; ++var5)
            {
                distanceToGap[var5] = 1000;
                var6 = x;
                int var8 = z;
                if (var5 == 0)
                {
                    var6 = x - 1;
                }

                if (var5 == 1)
                {
                    ++var6;
                }

                if (var5 == 2)
                {
                    var8 = z - 1;
                }

                if (var5 == 3)
                {
                    ++var8;
                }

                if (!isLiquidBreaking(world, var6, y, var8) && (world.getMaterial(var6, y, var8) != material || world.getBlockMeta(var6, y, var8) != 0))
                {
                    if (!isLiquidBreaking(world, var6, y - 1, var8))
                    {
                        distanceToGap[var5] = 0;
                    }
                    else
                    {
                        distanceToGap[var5] = getDistanceToGap(world, var6, y, var8, 1, var5);
                    }
                }
            }

            var5 = distanceToGap[0];

            for (var6 = 1; var6 < 4; ++var6)
            {
                if (distanceToGap[var6] < var5)
                {
                    var5 = distanceToGap[var6];
                }
            }

            for (var6 = 0; var6 < 4; ++var6)
            {
                spread[var6] = distanceToGap[var6] == var5;
            }

            return spread;
        }

        private bool isLiquidBreaking(World world, int x, int y, int z)
        {
            int var5 = world.getBlockId(x, y, z);
            if (var5 != Block.DOOR.id && var5 != Block.IRON_DOOR.id && var5 != Block.SIGN.id && var5 != Block.LADDER.id && var5 != Block.SUGAR_CANE.id)
            {
                if (var5 == 0)
                {
                    return false;
                }
                else
                {
                    Material var6 = Block.BLOCKS[var5].material;
                    return var6.blocksMovement();
                }
            }
            else
            {
                return true;
            }
        }

        protected int getLowestDepth(World world, int x, int y, int z, int depth)
        {
            int var6 = getLiquidState(world, x, y, z);
            if (var6 < 0)
            {
                return depth;
            }
            else
            {
                if (var6 == 0)
                {
                    ++adjacentSources;
                }

                if (var6 >= 8)
                {
                    var6 = 0;
                }

                return depth >= 0 && var6 >= depth ? depth : var6;
            }
        }

        private bool canSpreadTo(World world, int x, int y, int z)
        {
            Material var5 = world.getMaterial(x, y, z);
            return var5 == material ? false : (var5 == Material.LAVA ? false : !isLiquidBreaking(world, x, y, z));
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            base.onPlaced(world, x, y, z);
            if (world.getBlockId(x, y, z) == id)
            {
                world.scheduleBlockUpdate(x, y, z, id, getTickRate());
            }

        }
    }

}