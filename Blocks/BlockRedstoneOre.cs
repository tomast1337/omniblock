using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockRedstoneOre : Block
    {

        private bool lit;

        public BlockRedstoneOre(int id, int textureId, bool lit) : base(id, textureId, Material.STONE)
        {
            if (lit)
            {
                setTickRandomly(true);
            }

            this.lit = lit;
        }

        public override int getTickRate()
        {
            return 30;
        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer player)
        {
            light(world, x, y, z);
            base.onBlockBreakStart(world, x, y, z, player);
        }

        public override void onSteppedOn(World world, int x, int y, int z, Entity entity)
        {
            light(world, x, y, z);
            base.onSteppedOn(world, x, y, z, entity);
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            light(world, x, y, z);
            return base.onUse(world, x, y, z, player);
        }

        private void light(World world, int x, int y, int z)
        {
            spawnParticles(world, x, y, z);
            if (id == Block.REDSTONE_ORE.id)
            {
                world.setBlockWithNotify(x, y, z, Block.LIT_REDSTONE_ORE.id);
            }

        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (id == Block.LIT_REDSTONE_ORE.id)
            {
                world.setBlockWithNotify(x, y, z, Block.REDSTONE_ORE.id);
            }

        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.redstone.id;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 4 + random.nextInt(2);
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (lit)
            {
                spawnParticles(world, x, y, z);
            }

        }

        private void spawnParticles(World world, int x, int y, int z)
        {
            java.util.Random var5 = world.random;
            double var6 = 1.0D / 16.0D;

            for (int var8 = 0; var8 < 6; ++var8)
            {
                double var9 = (double)((float)x + var5.nextFloat());
                double var11 = (double)((float)y + var5.nextFloat());
                double var13 = (double)((float)z + var5.nextFloat());
                if (var8 == 0 && !world.isOpaque(x, y + 1, z))
                {
                    var11 = (double)(y + 1) + var6;
                }

                if (var8 == 1 && !world.isOpaque(x, y - 1, z))
                {
                    var11 = (double)(y + 0) - var6;
                }

                if (var8 == 2 && !world.isOpaque(x, y, z + 1))
                {
                    var13 = (double)(z + 1) + var6;
                }

                if (var8 == 3 && !world.isOpaque(x, y, z - 1))
                {
                    var13 = (double)(z + 0) - var6;
                }

                if (var8 == 4 && !world.isOpaque(x + 1, y, z))
                {
                    var9 = (double)(x + 1) + var6;
                }

                if (var8 == 5 && !world.isOpaque(x - 1, y, z))
                {
                    var9 = (double)(x + 0) - var6;
                }

                if (var9 < (double)x || var9 > (double)(x + 1) || var11 < 0.0D || var11 > (double)(y + 1) || var13 < (double)z || var13 > (double)(z + 1))
                {
                    world.addParticle("reddust", var9, var11, var13, 0.0D, 0.0D, 0.0D);
                }
            }

        }
    }

}