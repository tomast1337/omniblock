using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockMushroom : BlockPlant
    {
        public BlockMushroom(int i, int j) : base(i, j)
        {
            float var3 = 0.2F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, var3 * 2.0F, 0.5F + var3);
            setTickRandomly(true);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (random.nextInt(100) == 0)
            {
                int var6 = x + random.nextInt(3) - 1;
                int var7 = y + random.nextInt(2) - random.nextInt(2);
                int var8 = z + random.nextInt(3) - 1;
                if (world.isAir(var6, var7, var8) && canGrow(world, var6, var7, var8))
                {
                    int var10000 = x + (random.nextInt(3) - 1);
                    var10000 = z + (random.nextInt(3) - 1);
                    if (world.isAir(var6, var7, var8) && canGrow(world, var6, var7, var8))
                    {
                        world.setBlock(var6, var7, var8, id);
                    }
                }
            }

        }

        protected override bool canPlantOnTop(int id)
        {
            return Block.BLOCKS_OPAQUE[id];
        }

        public override bool canGrow(World world, int x, int y, int z)
        {
            return y >= 0 && y < 128 ? world.getBrightness(x, y, z) < 13 && canPlantOnTop(world.getBlockId(x, y - 1, z)) : false;
        }
    }

}