using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockPressurePlate : Block
    {

        private readonly PressurePlateActiviationRule activationRule;

        public BlockPressurePlate(int id, int textureId, PressurePlateActiviationRule rule, Material material) : base(id, textureId, material)
        {
            activationRule = rule;
            setTickRandomly(true);
            float var5 = 1.0F / 16.0F;
            setBoundingBox(var5, 0.0F, var5, 1.0F - var5, 0.03125F, 1.0F - var5);
        }

        public override int getTickRate()
        {
            return 20;
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x, y - 1, z);
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            bool var6 = false;
            if (!world.shouldSuffocate(x, y - 1, z))
            {
                var6 = true;
            }

            if (var6)
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (!world.isRemote)
            {
                if (world.getBlockMeta(x, y, z) != 0)
                {
                    updatePlateState(world, x, y, z);
                }
            }
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            if (!world.isRemote)
            {
                if (world.getBlockMeta(x, y, z) != 1)
                {
                    updatePlateState(world, x, y, z);
                }
            }
        }

        private void updatePlateState(World world, int x, int y, int z)
        {
            bool var5 = world.getBlockMeta(x, y, z) == 1;
            bool var6 = false;
            float var7 = 2.0F / 16.0F;
            List<Entity> var8 = null;
            if (activationRule == PressurePlateActiviationRule.EVERYTHING)
            {
                var8 = world.getEntitiesWithinAABBExcludingEntity((Entity)null, Box.createCached((double)((float)x + var7), (double)y, (double)((float)z + var7), (double)((float)(x + 1) - var7), (double)y + 0.25D, (double)((float)(z + 1) - var7)));
            }

            if (activationRule == PressurePlateActiviationRule.MOBS)
            {
                var8 = world.getEntitiesWithinAABB(EntityLiving.Class, Box.createCached((double)((float)x + var7), (double)y, (double)((float)z + var7), (double)((float)(x + 1) - var7), (double)y + 0.25D, (double)((float)(z + 1) - var7)));
            }

            if (activationRule == PressurePlateActiviationRule.PLAYERS)
            {
                var8 = world.getEntitiesWithinAABB(EntityPlayer.Class, Box.createCached((double)((float)x + var7), (double)y, (double)((float)z + var7), (double)((float)(x + 1) - var7), (double)y + 0.25D, (double)((float)(z + 1) - var7)));
            }

            if (var8.Count > 0)
            {
                var6 = true;
            }

            if (var6 && !var5)
            {
                world.setBlockMeta(x, y, z, 1);
                world.notifyNeighbors(x, y, z, id);
                world.notifyNeighbors(x, y - 1, z, id);
                world.setBlocksDirty(x, y, z, x, y, z);
                world.playSound((double)x + 0.5D, (double)y + 0.1D, (double)z + 0.5D, "random.click", 0.3F, 0.6F);
            }

            if (!var6 && var5)
            {
                world.setBlockMeta(x, y, z, 0);
                world.notifyNeighbors(x, y, z, id);
                world.notifyNeighbors(x, y - 1, z, id);
                world.setBlocksDirty(x, y, z, x, y, z);
                world.playSound((double)x + 0.5D, (double)y + 0.1D, (double)z + 0.5D, "random.click", 0.3F, 0.5F);
            }

            if (var6)
            {
                world.scheduleBlockUpdate(x, y, z, id, getTickRate());
            }

        }

        public override void onBreak(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            if (var5 > 0)
            {
                world.notifyNeighbors(x, y, z, id);
                world.notifyNeighbors(x, y - 1, z, id);
            }

            base.onBreak(world, x, y, z);
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            bool var5 = blockView.getBlockMeta(x, y, z) == 1;
            float var6 = 1.0F / 16.0F;
            if (var5)
            {
                setBoundingBox(var6, 0.0F, var6, 1.0F - var6, 0.03125F, 1.0F - var6);
            }
            else
            {
                setBoundingBox(var6, 0.0F, var6, 1.0F - var6, 1.0F / 16.0F, 1.0F - var6);
            }

        }

        public override bool isPoweringSide(BlockView blockView, int x, int y, int z, int side)
        {
            return blockView.getBlockMeta(x, y, z) > 0;
        }

        public override bool isStrongPoweringSide(World world, int x, int y, int z, int side)
        {
            return world.getBlockMeta(x, y, z) == 0 ? false : side == 1;
        }

        public override bool canEmitRedstonePower()
        {
            return true;
        }

        public override void setupRenderBoundingBox()
        {
            float var1 = 0.5F;
            float var2 = 2.0F / 16.0F;
            float var3 = 0.5F;
            setBoundingBox(0.5F - var1, 0.5F - var2, 0.5F - var3, 0.5F + var1, 0.5F + var2, 0.5F + var3);
        }

        public override int getPistonBehavior()
        {
            return 1;
        }
    }

}