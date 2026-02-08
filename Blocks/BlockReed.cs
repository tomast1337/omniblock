using betareborn.Blocks.Materials;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockReed : Block
    {

        public BlockReed(int id, int textureId) : base(id, Material.PLANT)
        {
            base.textureId = textureId;
            float var3 = 6.0F / 16.0F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, 1.0F, 0.5F + var3);
            setTickRandomly(true);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (world.isAir(x, y + 1, z))
            {
                int var6;
                for (var6 = 1; world.getBlockId(x, y - var6, z) == id; ++var6)
                {
                }

                if (var6 < 3)
                {
                    int var7 = world.getBlockMeta(x, y, z);
                    if (var7 == 15)
                    {
                        world.setBlockWithNotify(x, y + 1, z, id);
                        world.setBlockMeta(x, y, z, 0);
                    }
                    else
                    {
                        world.setBlockMeta(x, y, z, var7 + 1);
                    }
                }
            }

        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            int var5 = world.getBlockId(x, y - 1, z);
            return var5 == id ? true : (var5 != Block.GRASS_BLOCK.id && var5 != Block.DIRT.id ? false : (world.getMaterial(x - 1, y - 1, z) == Material.WATER ? true : (world.getMaterial(x + 1, y - 1, z) == Material.WATER ? true : (world.getMaterial(x, y - 1, z - 1) == Material.WATER ? true : world.getMaterial(x, y - 1, z + 1) == Material.WATER))));
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            breakIfCannotGrow(world, x, y, z);
        }

        protected void breakIfCannotGrow(World world, int x, int y, int z)
        {
            if (!canGrow(world, x, y, z))
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override bool canGrow(World world, int x, int y, int z)
        {
            return canPlaceAt(world, x, y, z);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.reed.id;
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
            return 1;
        }
    }

}