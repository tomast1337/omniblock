using betareborn.Blocks.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLadder : Block
    {

        public BlockLadder(int id, int textureId) : base(id, textureId, Material.PISTON_BREAKABLE)
        {
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            float var6 = 2.0F / 16.0F;
            if (var5 == 2)
            {
                setBoundingBox(0.0F, 0.0F, 1.0F - var6, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 3)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var6);
            }

            if (var5 == 4)
            {
                setBoundingBox(1.0F - var6, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 5)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, var6, 1.0F, 1.0F);
            }

            return base.getCollisionShape(world, x, y, z);
        }

        public override Box getBoundingBox(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            float var6 = 2.0F / 16.0F;
            if (var5 == 2)
            {
                setBoundingBox(0.0F, 0.0F, 1.0F - var6, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 3)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var6);
            }

            if (var5 == 4)
            {
                setBoundingBox(1.0F - var6, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (var5 == 5)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, var6, 1.0F, 1.0F);
            }

            return base.getBoundingBox(world, x, y, z);
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
            return 8;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x - 1, y, z) ? true : (world.shouldSuffocate(x + 1, y, z) ? true : (world.shouldSuffocate(x, y, z - 1) ? true : world.shouldSuffocate(x, y, z + 1)));
        }

        public override void onPlaced(World world, int x, int y, int z, int direction)
        {
            int var6 = world.getBlockMeta(x, y, z);
            if ((var6 == 0 || direction == 2) && world.shouldSuffocate(x, y, z + 1))
            {
                var6 = 2;
            }

            if ((var6 == 0 || direction == 3) && world.shouldSuffocate(x, y, z - 1))
            {
                var6 = 3;
            }

            if ((var6 == 0 || direction == 4) && world.shouldSuffocate(x + 1, y, z))
            {
                var6 = 4;
            }

            if ((var6 == 0 || direction == 5) && world.shouldSuffocate(x - 1, y, z))
            {
                var6 = 5;
            }

            world.setBlockMeta(x, y, z, var6);
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            int var6 = world.getBlockMeta(x, y, z);
            bool var7 = false;
            if (var6 == 2 && world.shouldSuffocate(x, y, z + 1))
            {
                var7 = true;
            }

            if (var6 == 3 && world.shouldSuffocate(x, y, z - 1))
            {
                var7 = true;
            }

            if (var6 == 4 && world.shouldSuffocate(x + 1, y, z))
            {
                var7 = true;
            }

            if (var6 == 5 && world.shouldSuffocate(x - 1, y, z))
            {
                var7 = true;
            }

            if (!var7)
            {
                dropStacks(world, x, y, z, var6);
                world.setBlockWithNotify(x, y, z, 0);
            }

            base.neighborUpdate(world, x, y, z, id);
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 1;
        }
    }

}