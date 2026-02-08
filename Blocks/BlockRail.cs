using betareborn.Blocks.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockRail : Block
    {

        private readonly bool alwaysStraight;

        public static bool isRail(World world, int x, int y, int z)
        {
            int var4 = world.getBlockId(x, y, z);
            return var4 == Block.RAIL.id || var4 == Block.POWERED_RAIL.id || var4 == Block.DETECTOR_RAIL.id;
        }

        public static bool isRail(int id)
        {
            return id == Block.RAIL.id || id == Block.POWERED_RAIL.id || id == Block.DETECTOR_RAIL.id;
        }

        public BlockRail(int id, int textureId, bool alwaysStraight) : base(id, textureId, Material.PISTON_BREAKABLE)
        {
            this.alwaysStraight = alwaysStraight;
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
        }

        public bool isAlwaysStraight()
        {
            return alwaysStraight;
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override HitResult raycast(World world, int x, int y, int z, Vec3D startPos, Vec3D endPos)
        {
            updateBoundingBox(world, x, y, z);
            return base.raycast(world, x, y, z, startPos, endPos);
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            int var5 = blockView.getBlockMeta(x, y, z);
            if (var5 >= 2 && var5 <= 5)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 10.0F / 16.0F, 1.0F);
            }
            else
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
            }

        }

        public override int getTexture(int side, int meta)
        {
            if (alwaysStraight)
            {
                if (id == Block.POWERED_RAIL.id && (meta & 8) == 0)
                {
                    return textureId - 16;
                }
            }
            else if (meta >= 6)
            {
                return textureId - 16;
            }

            return textureId;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 9;
        }

        public override bool canPlaceAt(World var1, int x, int y, int z)
        {
            return var1.shouldSuffocate(x, y - 1, z);
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            if (!world.isRemote)
            {
                updateShape(world, x, y, z, true);
            }

        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                int var7 = var6;
                if (alwaysStraight)
                {
                    var7 = var6 & 7;
                }

                bool var8 = false;
                if (!world.shouldSuffocate(x, y - 1, z))
                {
                    var8 = true;
                }

                if (var7 == 2 && !world.shouldSuffocate(x + 1, y, z))
                {
                    var8 = true;
                }

                if (var7 == 3 && !world.shouldSuffocate(x - 1, y, z))
                {
                    var8 = true;
                }

                if (var7 == 4 && !world.shouldSuffocate(x, y, z - 1))
                {
                    var8 = true;
                }

                if (var7 == 5 && !world.shouldSuffocate(x, y, z + 1))
                {
                    var8 = true;
                }

                if (var8)
                {
                    dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                    world.setBlockWithNotify(x, y, z, 0);
                }
                else if (base.id == Block.POWERED_RAIL.id)
                {
                    bool var9 = world.isPowered(x, y, z) || world.isPowered(x, y + 1, z);
                    var9 = var9 || isPoweredByConnectedRails(world, x, y, z, var6, true, 0) || isPoweredByConnectedRails(world, x, y, z, var6, false, 0);
                    bool var10 = false;
                    if (var9 && (var6 & 8) == 0)
                    {
                        world.setBlockMeta(x, y, z, var7 | 8);
                        var10 = true;
                    }
                    else if (!var9 && (var6 & 8) != 0)
                    {
                        world.setBlockMeta(x, y, z, var7);
                        var10 = true;
                    }

                    if (var10)
                    {
                        world.notifyNeighbors(x, y - 1, z, base.id);
                        if (var7 == 2 || var7 == 3 || var7 == 4 || var7 == 5)
                        {
                            world.notifyNeighbors(x, y + 1, z, base.id);
                        }
                    }
                }
                else if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower() && !alwaysStraight && RailLogic.getNAdjacentTracks(new RailLogic(this, world, x, y, z)) == 3)
                {
                    updateShape(world, x, y, z, false);
                }

            }
        }

        private void updateShape(World world, int x, int y, int z, bool force)
        {
            if (!world.isRemote)
            {
                (new RailLogic(this, world, x, y, z)).updateState(world.isPowered(x, y, z), force);
            }
        }

        private bool isPoweredByConnectedRails(World world, int x, int y, int z, int meta, bool towardsNegative, int depth)
        {
            if (depth >= 8)
            {
                return false;
            }
            else
            {
                int var8 = meta & 7;
                bool var9 = true;
                switch (var8)
                {
                    case 0:
                        if (towardsNegative)
                        {
                            ++z;
                        }
                        else
                        {
                            --z;
                        }
                        break;
                    case 1:
                        if (towardsNegative)
                        {
                            --x;
                        }
                        else
                        {
                            ++x;
                        }
                        break;
                    case 2:
                        if (towardsNegative)
                        {
                            --x;
                        }
                        else
                        {
                            ++x;
                            ++y;
                            var9 = false;
                        }

                        var8 = 1;
                        break;
                    case 3:
                        if (towardsNegative)
                        {
                            --x;
                            ++y;
                            var9 = false;
                        }
                        else
                        {
                            ++x;
                        }

                        var8 = 1;
                        break;
                    case 4:
                        if (towardsNegative)
                        {
                            ++z;
                        }
                        else
                        {
                            --z;
                            ++y;
                            var9 = false;
                        }

                        var8 = 0;
                        break;
                    case 5:
                        if (towardsNegative)
                        {
                            ++z;
                            ++y;
                            var9 = false;
                        }
                        else
                        {
                            --z;
                        }

                        var8 = 0;
                        break;
                }

                return isPoweredByRail(world, x, y, z, towardsNegative, depth, var8) ? true : var9 && isPoweredByRail(world, x, y - 1, z, towardsNegative, depth, var8);
            }
        }

        private bool isPoweredByRail(World world, int x, int y, int z, bool towardsNegative, int depth, int shape)
        {
            int var8 = world.getBlockId(x, y, z);
            if (var8 == Block.POWERED_RAIL.id)
            {
                int var9 = world.getBlockMeta(x, y, z);
                int var10 = var9 & 7;
                if (shape == 1 && (var10 == 0 || var10 == 4 || var10 == 5))
                {
                    return false;
                }

                if (shape == 0 && (var10 == 1 || var10 == 2 || var10 == 3))
                {
                    return false;
                }

                if ((var9 & 8) != 0)
                {
                    if (!world.isPowered(x, y, z) && !world.isPowered(x, y + 1, z))
                    {
                        return isPoweredByConnectedRails(world, x, y, z, var9, towardsNegative, depth + 1);
                    }

                    return true;
                }
            }

            return false;
        }

        public override int getPistonBehavior()
        {
            return 0;
        }
    }

}