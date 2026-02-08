using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockFarmland : Block
    {

        public BlockFarmland(int id) : base(id, Material.SOIL)
        {
            textureId = 87;
            setTickRandomly(true);
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 15.0F / 16.0F, 1.0F);
            setOpacity(255);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return Box.createCached((double)(x + 0), (double)(y + 0), (double)(z + 0), (double)(x + 1), (double)(y + 1), (double)(z + 1));
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override int getTexture(int side, int meta)
        {
            return side == 1 && meta > 0 ? textureId - 1 : (side == 1 ? textureId : 2);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (random.nextInt(5) == 0)
            {
                if (!isWaterNearby(world, x, y, z) && !world.isRaining(x, y + 1, z))
                {
                    int var6 = world.getBlockMeta(x, y, z);
                    if (var6 > 0)
                    {
                        world.setBlockMeta(x, y, z, var6 - 1);
                    }
                    else if (!hasCrop(world, x, y, z))
                    {
                        world.setBlockWithNotify(x, y, z, Block.DIRT.id);
                    }
                }
                else
                {
                    world.setBlockMeta(x, y, z, 7);
                }
            }

        }

        public override void onSteppedOn(World world, int x, int y, int z, Entity entity)
        {
            if (world.random.nextInt(4) == 0)
            {
                world.setBlockWithNotify(x, y, z, Block.DIRT.id);
            }

        }

        private static bool hasCrop(World world, int x, int y, int z)
        {
            sbyte var5 = 0;

            for (int var6 = x - var5; var6 <= x + var5; ++var6)
            {
                for (int var7 = z - var5; var7 <= z + var5; ++var7)
                {
                    if (world.getBlockId(var6, y + 1, var7) == Block.WHEAT.id)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool isWaterNearby(World world, int x, int y, int z)
        {
            for (int var5 = x - 4; var5 <= x + 4; ++var5)
            {
                for (int var6 = y; var6 <= y + 1; ++var6)
                {
                    for (int var7 = z - 4; var7 <= z + 4; ++var7)
                    {
                        if (world.getMaterial(var5, var6, var7) == Material.WATER)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            base.neighborUpdate(world, x, y, z, id);
            Material var6 = world.getMaterial(x, y + 1, z);
            if (var6.isSolid())
            {
                world.setBlockWithNotify(x, y, z, Block.DIRT.id);
            }

        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.DIRT.getDroppedItemId(0, random);
        }
    }

}