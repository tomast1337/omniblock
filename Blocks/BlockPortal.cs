using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockPortal : BlockBreakable
    {

        public BlockPortal(int id, int textureId) : base(id, textureId, Material.NETHER_PORTAL, false)
        {
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            float var5;
            float var6;
            if (blockView.getBlockId(x - 1, y, z) != id && blockView.getBlockId(x + 1, y, z) != id)
            {
                var5 = 2.0F / 16.0F;
                var6 = 0.5F;
                setBoundingBox(0.5F - var5, 0.0F, 0.5F - var6, 0.5F + var5, 1.0F, 0.5F + var6);
            }
            else
            {
                var5 = 0.5F;
                var6 = 2.0F / 16.0F;
                setBoundingBox(0.5F - var5, 0.0F, 0.5F - var6, 0.5F + var5, 1.0F, 0.5F + var6);
            }

        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public bool create(World world, int x, int y, int z)
        {
            sbyte var5 = 0;
            sbyte var6 = 0;
            if (world.getBlockId(x - 1, y, z) == Block.OBSIDIAN.id || world.getBlockId(x + 1, y, z) == Block.OBSIDIAN.id)
            {
                var5 = 1;
            }

            if (world.getBlockId(x, y, z - 1) == Block.OBSIDIAN.id || world.getBlockId(x, y, z + 1) == Block.OBSIDIAN.id)
            {
                var6 = 1;
            }

            if (var5 == var6)
            {
                return false;
            }
            else
            {
                if (world.getBlockId(x - var5, y, z - var6) == 0)
                {
                    x -= var5;
                    z -= var6;
                }

                int var7;
                int var8;
                for (var7 = -1; var7 <= 2; ++var7)
                {
                    for (var8 = -1; var8 <= 3; ++var8)
                    {
                        bool var9 = var7 == -1 || var7 == 2 || var8 == -1 || var8 == 3;
                        if (var7 != -1 && var7 != 2 || var8 != -1 && var8 != 3)
                        {
                            int var10 = world.getBlockId(x + var5 * var7, y + var8, z + var6 * var7);
                            if (var9)
                            {
                                if (var10 != Block.OBSIDIAN.id)
                                {
                                    return false;
                                }
                            }
                            else if (var10 != 0 && var10 != Block.FIRE.id)
                            {
                                return false;
                            }
                        }
                    }
                }

                world.pauseTicking = true;

                for (var7 = 0; var7 < 2; ++var7)
                {
                    for (var8 = 0; var8 < 3; ++var8)
                    {
                        world.setBlockWithNotify(x + var5 * var7, y + var8, z + var6 * var7, Block.NETHER_PORTAL.id);
                    }
                }

                world.pauseTicking = false;
                return true;
            }
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            sbyte var6 = 0;
            sbyte var7 = 1;
            if (world.getBlockId(x - 1, y, z) == base.id || world.getBlockId(x + 1, y, z) == base.id)
            {
                var6 = 1;
                var7 = 0;
            }

            int var8;
            for (var8 = y; world.getBlockId(x, var8 - 1, z) == base.id; --var8)
            {
            }

            if (world.getBlockId(x, var8 - 1, z) != Block.OBSIDIAN.id)
            {
                world.setBlockWithNotify(x, y, z, 0);
            }
            else
            {
                int var9;
                for (var9 = 1; var9 < 4 && world.getBlockId(x, var8 + var9, z) == base.id; ++var9)
                {
                }

                if (var9 == 3 && world.getBlockId(x, var8 + var9, z) == Block.OBSIDIAN.id)
                {
                    bool var10 = world.getBlockId(x - 1, y, z) == base.id || world.getBlockId(x + 1, y, z) == base.id;
                    bool var11 = world.getBlockId(x, y, z - 1) == base.id || world.getBlockId(x, y, z + 1) == base.id;
                    if (var10 && var11)
                    {
                        world.setBlockWithNotify(x, y, z, 0);
                    }
                    else if ((world.getBlockId(x + var6, y, z + var7) != Block.OBSIDIAN.id || world.getBlockId(x - var6, y, z - var7) != base.id) && (world.getBlockId(x - var6, y, z - var7) != Block.OBSIDIAN.id || world.getBlockId(x + var6, y, z + var7) != base.id))
                    {
                        world.setBlockWithNotify(x, y, z, 0);
                    }
                }
                else
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }
            }
        }

        public override bool isSideVisible(BlockView blockView, int x, int y, int z, int side)
        {
            if (blockView.getBlockId(x, y, z) == id)
            {
                return false;
            }
            else
            {
                bool var6 = blockView.getBlockId(x - 1, y, z) == id && blockView.getBlockId(x - 2, y, z) != id;
                bool var7 = blockView.getBlockId(x + 1, y, z) == id && blockView.getBlockId(x + 2, y, z) != id;
                bool var8 = blockView.getBlockId(x, y, z - 1) == id && blockView.getBlockId(x, y, z - 2) != id;
                bool var9 = blockView.getBlockId(x, y, z + 1) == id && blockView.getBlockId(x, y, z + 2) != id;
                bool var10 = var6 || var7;
                bool var11 = var8 || var9;
                return var10 && side == 4 ? true : (var10 && side == 5 ? true : (var11 && side == 2 ? true : var11 && side == 3));
            }
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        public override int getRenderLayer()
        {
            return 1;
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            if (entity.ridingEntity == null && entity.riddenByEntity == null)
            {
                entity.setInPortal();
            }

        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (random.nextInt(100) == 0)
            {
                world.playSound((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D, "portal.portal", 1.0F, random.nextFloat() * 0.4F + 0.8F);
            }

            for (int var6 = 0; var6 < 4; ++var6)
            {
                double var7 = (double)((float)x + random.nextFloat());
                double var9 = (double)((float)y + random.nextFloat());
                double var11 = (double)((float)z + random.nextFloat());
                double var13 = 0.0D;
                double var15 = 0.0D;
                double var17 = 0.0D;
                int var19 = random.nextInt(2) * 2 - 1;
                var13 = ((double)random.nextFloat() - 0.5D) * 0.5D;
                var15 = ((double)random.nextFloat() - 0.5D) * 0.5D;
                var17 = ((double)random.nextFloat() - 0.5D) * 0.5D;
                if (world.getBlockId(x - 1, y, z) != id && world.getBlockId(x + 1, y, z) != id)
                {
                    var7 = (double)x + 0.5D + 0.25D * (double)var19;
                    var13 = (double)(random.nextFloat() * 2.0F * (float)var19);
                }
                else
                {
                    var11 = (double)z + 0.5D + 0.25D * (double)var19;
                    var17 = (double)(random.nextFloat() * 2.0F * (float)var19);
                }

                world.addParticle("portal", var7, var9, var11, var13, var15, var17);
            }

        }
    }

}