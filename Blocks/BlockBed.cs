using betareborn.Blocks.Materials;
using betareborn.Chunks;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Models;
using betareborn.Worlds;
using java.util;

namespace betareborn.Blocks
{
    public class BlockBed : Block
    {
        public static readonly int[][] BED_OFFSETS = [[0, 1], [-1, 0], [0, -1], [1, 0]];

        public BlockBed(int id) : base(id, 134, Material.WOOL)
        {
            setDefaultShape();
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (world.isRemote)
            {
                return true;
            }
            else
            {
                int var6 = world.getBlockMeta(x, y, z);
                if (!isHeadOfBed(var6))
                {
                    int var7 = getDirection(var6);
                    x += BED_OFFSETS[var7][0];
                    z += BED_OFFSETS[var7][1];
                    if (world.getBlockId(x, y, z) != id)
                    {
                        return true;
                    }

                    var6 = world.getBlockMeta(x, y, z);
                }

                if (!world.dimension.hasWorldSpawn())
                {
                    double var16 = (double)x + 0.5D;
                    double var17 = (double)y + 0.5D;
                    double var11 = (double)z + 0.5D;
                    world.setBlockWithNotify(x, y, z, 0);
                    int var13 = getDirection(var6);
                    x += BED_OFFSETS[var13][0];
                    z += BED_OFFSETS[var13][1];
                    if (world.getBlockId(x, y, z) == id)
                    {
                        world.setBlockWithNotify(x, y, z, 0);
                        var16 = (var16 + (double)x + 0.5D) / 2.0D;
                        var17 = (var17 + (double)y + 0.5D) / 2.0D;
                        var11 = (var11 + (double)z + 0.5D) / 2.0D;
                    }

                    world.newExplosion((Entity)null, (double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), 5.0F, true);
                    return true;
                }
                else
                {
                    if (isBedOccupied(var6))
                    {
                        EntityPlayer var14 = null;
                        Iterator var8 = world.playerEntities.iterator();

                        while (var8.hasNext())
                        {
                            EntityPlayer var9 = (EntityPlayer)var8.next();
                            if (var9.isPlayerSleeping())
                            {
                                Vec3i var10 = var9.bedChunkCoordinates;
                                if (var10.x == x && var10.y == y && var10.z == z)
                                {
                                    var14 = var9;
                                }
                            }
                        }

                        if (var14 != null)
                        {
                            player.addChatMessage("tile.bed.occupied");
                            return true;
                        }

                        updateState(world, x, y, z, false);
                    }

                    EnumStatus var15 = player.sleepInBedAt(x, y, z);
                    if (var15 == EnumStatus.OK)
                    {
                        updateState(world, x, y, z, true);
                        return true;
                    }
                    else
                    {
                        if (var15 == EnumStatus.NOT_POSSIBLE_NOW)
                        {
                            player.addChatMessage("tile.bed.noSleep");
                        }

                        return true;
                    }
                }
            }
        }

        public override int getTexture(int side, int meta)
        {
            if (side == 0)
            {
                return Block.PLANKS.textureId;
            }
            else
            {
                int var3 = getDirection(meta);
                int var4 = ModelBed.bedDirection[var3][side];
                return isHeadOfBed(meta) ? (var4 == 2 ? textureId + 2 + 16 : (var4 != 5 && var4 != 4 ? textureId + 1 : textureId + 1 + 16)) : (var4 == 3 ? textureId - 1 + 16 : (var4 != 5 && var4 != 4 ? textureId : textureId + 16));
            }
        }

        public override int getRenderType()
        {
            return 14;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override void updateBoundingBox(BlockView var1, int x, int y, int z)
        {
            setDefaultShape();
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            int var6 = world.getBlockMeta(x, y, z);
            int var7 = getDirection(var6);
            if (isHeadOfBed(var6))
            {
                if (world.getBlockId(x - BED_OFFSETS[var7][0], y, z - BED_OFFSETS[var7][1]) != this.id)
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }
            }
            else if (world.getBlockId(x + BED_OFFSETS[var7][0], y, z + BED_OFFSETS[var7][1]) != this.id)
            {
                world.setBlockWithNotify(x, y, z, 0);
                if (!world.isRemote)
                {
                    dropStacks(world, x, y, z, var6);
                }
            }

        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return isHeadOfBed(blockMeta) ? 0 : Item.bed.id;
        }

        private void setDefaultShape()
        {
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 9.0F / 16.0F, 1.0F);
        }

        public static int getDirection(int meta)
        {
            return meta & 3;
        }

        public static bool isHeadOfBed(int meta)
        {
            return (meta & 8) != 0;
        }

        public static bool isBedOccupied(int meta)
        {
            return (meta & 4) != 0;
        }

        public static void updateState(World world, int x, int y, int z, bool occupied)
        {
            int var5 = world.getBlockMeta(x, y, z);
            if (occupied)
            {
                var5 |= 4;
            }
            else
            {
                var5 &= -5;
            }

            world.setBlockMeta(x, y, z, var5);
        }

        public static Vec3i findWakeUpPosition(World world, int x, int y, int z, int skip)
        {
            int var5 = world.getBlockMeta(x, y, z);
            int var6 = getDirection(var5);

            for (int var7 = 0; var7 <= 1; ++var7)
            {
                int var8 = x - BED_OFFSETS[var6][0] * var7 - 1;
                int var9 = z - BED_OFFSETS[var6][1] * var7 - 1;
                int var10 = var8 + 2;
                int var11 = var9 + 2;

                for (int var12 = var8; var12 <= var10; ++var12)
                {
                    for (int var13 = var9; var13 <= var11; ++var13)
                    {
                        if (world.shouldSuffocate(var12, y - 1, var13) && world.isAir(var12, y, var13) && world.isAir(var12, y + 1, var13))
                        {
                            if (skip <= 0)
                            {
                                return new Vec3i(var12, y, var13);
                            }

                            --skip;
                        }
                    }
                }
            }

            return null;
        }

        public override void dropStacks(World world, int x, int y, int z, int meta, float luck)
        {
            if (!isHeadOfBed(meta))
            {
                base.dropStacks(world, x, y, z, meta, luck);
            }

        }

        public override int getPistonBehavior()
        {
            return 1;
        }
    }

}