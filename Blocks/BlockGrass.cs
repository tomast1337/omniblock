using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockGrass : Block
    {
        public BlockGrass(int id) : base(id, Material.SOLID_ORGANIC)
        {
            textureId = 3;
            setTickRandomly(true);
        }

        public override int getTextureId(BlockView blockView, int x, int y, int z, int side)
        {
            if (side == 1)
            {
                return 0;
            }
            else if (side == 0)
            {
                return 2;
            }
            else
            {
                Material var6 = blockView.getMaterial(x, y + 1, z);
                return var6 != Material.SNOW_LAYER && var6 != Material.SNOW_BLOCK ? 3 : 68;
            }
        }

        public override int getColorMultiplier(BlockView blockView, int x, int y, int z)
        {
            blockView.getBiomeSource().getBiomesInArea(x, z, 1, 1);
            double var5 = blockView.getBiomeSource().temperatureMap[0];
            double var7 = blockView.getBiomeSource().downfallMap[0];
            return GrassColors.getColor(var5, var7);
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (!world.isRemote)
            {
                if (world.getLightLevel(x, y + 1, z) < 4 && Block.BLOCK_LIGHT_OPACITY[world.getBlockId(x, y + 1, z)] > 2)
                {
                    if (random.nextInt(4) != 0)
                    {
                        return;
                    }

                    world.setBlockWithNotify(x, y, z, Block.DIRT.id);
                }
                else if (world.getLightLevel(x, y + 1, z) >= 9)
                {
                    int var6 = x + random.nextInt(3) - 1;
                    int var7 = y + random.nextInt(5) - 3;
                    int var8 = z + random.nextInt(3) - 1;
                    int var9 = world.getBlockId(var6, var7 + 1, var8);
                    if (world.getBlockId(var6, var7, var8) == Block.DIRT.id && world.getLightLevel(var6, var7 + 1, var8) >= 4 && Block.BLOCK_LIGHT_OPACITY[var9] <= 2)
                    {
                        world.setBlockWithNotify(var6, var7, var8, Block.GRASS_BLOCK.id);
                    }
                }

            }
        }

        public override int getDroppedItemId(int blocKMeta, java.util.Random random)
        {
            return Block.DIRT.getDroppedItemId(0, random);
        }
    }

}