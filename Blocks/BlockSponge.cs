using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockSponge : Block
    {
        public BlockSponge(int id) : base(id, Material.SPONGE)
        {
            textureId = 48;
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            sbyte var5 = 2;

            for (int var6 = x - var5; var6 <= x + var5; ++var6)
            {
                for (int var7 = y - var5; var7 <= y + var5; ++var7)
                {
                    for (int var8 = z - var5; var8 <= z + var5; ++var8)
                    {
                        if (world.getMaterial(var6, var7, var8) == Material.WATER)
                        {
                        }
                    }
                }
            }

        }

        public override void onBreak(World world, int x, int y, int z)
        {
            sbyte var5 = 2;

            for (int var6 = x - var5; var6 <= x + var5; ++var6)
            {
                for (int var7 = y - var5; var7 <= y + var5; ++var7)
                {
                    for (int var8 = z - var5; var8 <= z + var5; ++var8)
                    {
                        world.notifyNeighbors(var6, var7, var8, world.getBlockId(var6, var7, var8));
                    }
                }
            }

        }
    }

}