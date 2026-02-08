using betareborn.Blocks.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockTorch : Block
    {

        public BlockTorch(int id, int textureId) : base(id, textureId, Material.PISTON_BREAKABLE)
        {
            setTickRandomly(true);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 2;
        }

        private bool canPlaceOn(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x, y, z) || world.getBlockId(x, y, z) == Block.FENCE.id;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x - 1, y, z) ? true : (world.shouldSuffocate(x + 1, y, z) ? true : (world.shouldSuffocate(x, y, z - 1) ? true : (world.shouldSuffocate(x, y, z + 1) ? true : canPlaceOn(world, x, y - 1, z))));
        }

        public override void onPlaced(World world, int x, int y, int z, int direction)
        {
            int var6 = world.getBlockMeta(x, y, z);
            if (direction == 1 && canPlaceOn(world, x, y - 1, z))
            {
                var6 = 5;
            }

            if (direction == 2 && world.shouldSuffocate(x, y, z + 1))
            {
                var6 = 4;
            }

            if (direction == 3 && world.shouldSuffocate(x, y, z - 1))
            {
                var6 = 3;
            }

            if (direction == 4 && world.shouldSuffocate(x + 1, y, z))
            {
                var6 = 2;
            }

            if (direction == 5 && world.shouldSuffocate(x - 1, y, z))
            {
                var6 = 1;
            }

            world.setBlockMeta(x, y, z, var6);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            base.onTick(world, x, y, z, random);
            if (world.getBlockMeta(x, y, z) == 0)
            {
                onPlaced(world, x, y, z);
            }

        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            if (world.shouldSuffocate(x - 1, y, z))
            {
                world.setBlockMeta(x, y, z, 1);
            }
            else if (world.shouldSuffocate(x + 1, y, z))
            {
                world.setBlockMeta(x, y, z, 2);
            }
            else if (world.shouldSuffocate(x, y, z - 1))
            {
                world.setBlockMeta(x, y, z, 3);
            }
            else if (world.shouldSuffocate(x, y, z + 1))
            {
                world.setBlockMeta(x, y, z, 4);
            }
            else if (canPlaceOn(world, x, y - 1, z))
            {
                world.setBlockMeta(x, y, z, 5);
            }

            breakIfCannotPlaceAt(world, x, y, z);
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (breakIfCannotPlaceAt(world, x, y, z))
            {
                int var6 = world.getBlockMeta(x, y, z);
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

                if (!canPlaceOn(world, x, y - 1, z) && var6 == 5)
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

        public override HitResult raycast(World world, int x, int y, int z, Vec3D startPos, Vec3D endPos)
        {
            int var7 = world.getBlockMeta(x, y, z) & 7;
            float var8 = 0.15F;
            if (var7 == 1)
            {
                setBoundingBox(0.0F, 0.2F, 0.5F - var8, var8 * 2.0F, 0.8F, 0.5F + var8);
            }
            else if (var7 == 2)
            {
                setBoundingBox(1.0F - var8 * 2.0F, 0.2F, 0.5F - var8, 1.0F, 0.8F, 0.5F + var8);
            }
            else if (var7 == 3)
            {
                setBoundingBox(0.5F - var8, 0.2F, 0.0F, 0.5F + var8, 0.8F, var8 * 2.0F);
            }
            else if (var7 == 4)
            {
                setBoundingBox(0.5F - var8, 0.2F, 1.0F - var8 * 2.0F, 0.5F + var8, 0.8F, 1.0F);
            }
            else
            {
                var8 = 0.1F;
                setBoundingBox(0.5F - var8, 0.0F, 0.5F - var8, 0.5F + var8, 0.6F, 0.5F + var8);
            }

            return base.raycast(world, x, y, z, startPos, endPos);
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            int var6 = world.getBlockMeta(x, y, z);
            double var7 = (double)((float)x + 0.5F);
            double var9 = (double)((float)y + 0.7F);
            double var11 = (double)((float)z + 0.5F);
            double var13 = (double)0.22F;
            double var15 = (double)0.27F;
            if (var6 == 1)
            {
                world.addParticle("smoke", var7 - var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var7 - var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
            }
            else if (var6 == 2)
            {
                world.addParticle("smoke", var7 + var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var7 + var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
            }
            else if (var6 == 3)
            {
                world.addParticle("smoke", var7, var9 + var13, var11 - var15, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var7, var9 + var13, var11 - var15, 0.0D, 0.0D, 0.0D);
            }
            else if (var6 == 4)
            {
                world.addParticle("smoke", var7, var9 + var13, var11 + var15, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var7, var9 + var13, var11 + var15, 0.0D, 0.0D, 0.0D);
            }
            else
            {
                world.addParticle("smoke", var7, var9, var11, 0.0D, 0.0D, 0.0D);
                world.addParticle("flame", var7, var9, var11, 0.0D, 0.0D, 0.0D);
            }

        }
    }

}