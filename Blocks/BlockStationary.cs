using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockStationary : BlockFluid
    {
        public BlockStationary(int id, Material material) : base(id, material)
        {
            setTickRandomly(false);
            if (material == Material.LAVA)
            {
                setTickRandomly(true);
            }

        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            base.neighborUpdate(world, x, y, z, id);
            if (world.getBlockId(x, y, z) == base.id)
            {
                convertToFlowing(world, x, y, z);
            }

        }

        private void convertToFlowing(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            world.pauseTicking = true;
            world.setBlockAndMetadata(x, y, z, id - 1, var5);
            world.setBlocksDirty(x, y, z, x, y, z);
            world.scheduleBlockUpdate(x, y, z, id - 1, getTickRate());
            world.pauseTicking = false;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (material == Material.LAVA)
            {
                int var6 = random.nextInt(3);

                for (int var7 = 0; var7 < var6; ++var7)
                {
                    x += random.nextInt(3) - 1;
                    ++y;
                    z += random.nextInt(3) - 1;
                    int var8 = world.getBlockId(x, y, z);
                    if (var8 == 0)
                    {
                        if (isFlammable(world, x - 1, y, z) || isFlammable(world, x + 1, y, z) || isFlammable(world, x, y, z - 1) || isFlammable(world, x, y, z + 1) || isFlammable(world, x, y - 1, z) || isFlammable(world, x, y + 1, z))
                        {
                            world.setBlockWithNotify(x, y, z, Block.FIRE.id);
                            return;
                        }
                    }
                    else if (Block.BLOCKS[var8].material.blocksMovement())
                    {
                        return;
                    }
                }
            }

        }

        private bool isFlammable(World world, int x, int y, int z)
        {
            return world.getMaterial(x, y, z).isBurnable();
        }
    }

}