using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockSand : Block
    {
        public static bool fallInstantly = false;

        public BlockSand(int id, int textureId) : base(id, textureId, Material.SAND)
        {
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            world.scheduleBlockUpdate(x, y, z, id, getTickRate());
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            world.scheduleBlockUpdate(x, y, z, base.id, getTickRate());
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            processFall(world, x, y, z);
        }

        private void processFall(World world, int x, int y, int z)
        {
            if (canFallThrough(world, x, y - 1, z) && y >= 0)
            {
                sbyte var8 = 32;
                if (!fallInstantly && world.checkChunksExist(x - var8, y - var8, z - var8, x + var8, y + var8, z + var8))
                {
                    EntityFallingSand var9 = new EntityFallingSand(world, (double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), id);
                    world.spawnEntity(var9);
                }
                else
                {
                    world.setBlockWithNotify(x, y, z, 0);

                    while (canFallThrough(world, x, y - 1, z) && y > 0)
                    {
                        --y;
                    }

                    if (y > 0)
                    {
                        world.setBlockWithNotify(x, y, z, id);
                    }
                }
            }

        }

        public override int getTickRate()
        {
            return 3;
        }

        public static bool canFallThrough(World world, int x, int y, int z)
        {
            int var4 = world.getBlockId(x, y, z);
            if (var4 == 0)
            {
                return true;
            }
            else if (var4 == Block.FIRE.id)
            {
                return true;
            }
            else
            {
                Material var5 = Block.BLOCKS[var4].material;
                return var5 == Material.WATER ? true : var5 == Material.LAVA;
            }
        }
    }

}