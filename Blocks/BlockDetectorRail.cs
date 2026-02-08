using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockDetectorRail : BlockRail
    {
        public BlockDetectorRail(int id, int textureId) : base(id, textureId, true)
        {
            setTickRandomly(true);
        }

        public override int getTickRate()
        {
            return 20;
        }

        public override bool canEmitRedstonePower()
        {
            return true;
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if ((var6 & 8) == 0)
                {
                    updatePoweredStatus(world, x, y, z, var6);
                }
            }
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if ((var6 & 8) != 0)
                {
                    updatePoweredStatus(world, x, y, z, var6);
                }
            }
        }

        public override bool isPoweringSide(BlockView blockView, int x, int y, int z, int side)
        {
            return (blockView.getBlockMeta(x, y, z) & 8) != 0;
        }

        public override bool isStrongPoweringSide(World world, int x, int y, int z, int side)
        {
            return (world.getBlockMeta(x, y, z) & 8) == 0 ? false : side == 1;
        }

        private void updatePoweredStatus(World world, int x, int y, int z, int meta)
        {
            bool var6 = (meta & 8) != 0;
            bool var7 = false;
            float var8 = 2.0F / 16.0F;
            var var9 = world.getEntitiesWithinAABB(EntityMinecart.Class, new Box((double)((float)x + var8), (double)y, (double)((float)z + var8), (double)((float)(x + 1) - var8), (double)y + 0.25D, (double)((float)(z + 1) - var8)));
            if (var9.Count > 0)
            {
                var7 = true;
            }

            if (var7 && !var6)
            {
                world.setBlockMeta(x, y, z, meta | 8);
                world.notifyNeighbors(x, y, z, id);
                world.notifyNeighbors(x, y - 1, z, id);
                world.setBlocksDirty(x, y, z, x, y, z);
            }

            if (!var7 && var6)
            {
                world.setBlockMeta(x, y, z, meta & 7);
                world.notifyNeighbors(x, y, z, id);
                world.notifyNeighbors(x, y - 1, z, id);
                world.setBlocksDirty(x, y, z, x, y, z);
            }

            if (var7)
            {
                world.scheduleBlockUpdate(x, y, z, id, getTickRate());
            }

        }
    }

}