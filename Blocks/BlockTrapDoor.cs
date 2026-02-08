using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockTrapDoor : Block
    {

        public BlockTrapDoor(int id, Material material) : base(id, material)
        {
            textureId = 84;
            if (material == Material.METAL)
            {
                ++textureId;
            }

            float var3 = 0.5F;
            float var4 = 1.0F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, var4, 0.5F + var3);
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
            return 0;
        }

        public override Box getBoundingBox(World world, int x, int y, int z)
        {
            updateBoundingBox(world, x, y, z);
            return base.getBoundingBox(world, x, y, z);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            updateBoundingBox(world, x, y, z);
            return base.getCollisionShape(world, x, y, z);
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            updateBoundingBox(blockView.getBlockMeta(x, y, z));
        }

        public override void setupRenderBoundingBox()
        {
            float var1 = 3.0F / 16.0F;
            setBoundingBox(0.0F, 0.5F - var1 / 2.0F, 0.0F, 1.0F, 0.5F + var1 / 2.0F, 1.0F);
        }

        public void updateBoundingBox(int meta)
        {
            float var2 = 3.0F / 16.0F;
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, var2, 1.0F);
            if (isOpen(meta))
            {
                if ((meta & 3) == 0)
                {
                    setBoundingBox(0.0F, 0.0F, 1.0F - var2, 1.0F, 1.0F, 1.0F);
                }

                if ((meta & 3) == 1)
                {
                    setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var2);
                }

                if ((meta & 3) == 2)
                {
                    setBoundingBox(1.0F - var2, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                }

                if ((meta & 3) == 3)
                {
                    setBoundingBox(0.0F, 0.0F, 0.0F, var2, 1.0F, 1.0F);
                }
            }

        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer player)
        {
            onUse(world, x, y, z, player);
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (material == Material.METAL)
            {
                return true;
            }
            else
            {
                int var6 = world.getBlockMeta(x, y, z);
                world.setBlockMeta(x, y, z, var6 ^ 4);
                world.worldEvent(player, 1003, x, y, z, 0);
                return true;
            }
        }

        public void setOpen(World world, int x, int y, int z, bool open)
        {
            int var6 = world.getBlockMeta(x, y, z);
            bool var7 = (var6 & 4) > 0;
            if (var7 != open)
            {
                world.setBlockMeta(x, y, z, var6 ^ 4);
                world.worldEvent((EntityPlayer)null, 1003, x, y, z, 0);
            }
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                int var7 = x;
                int var8 = z;
                if ((var6 & 3) == 0)
                {
                    var8 = z + 1;
                }

                if ((var6 & 3) == 1)
                {
                    --var8;
                }

                if ((var6 & 3) == 2)
                {
                    var7 = x + 1;
                }

                if ((var6 & 3) == 3)
                {
                    --var7;
                }

                if (!world.shouldSuffocate(var7, y, var8))
                {
                    world.setBlockWithNotify(x, y, z, 0);
                    dropStacks(world, x, y, z, var6);
                }

                if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower())
                {
                    bool var9 = world.isPowered(x, y, z);
                    setOpen(world, x, y, z, var9);
                }

            }
        }

        public override HitResult raycast(World world, int x, int y, int z, Vec3D startPos, Vec3D endPos)
        {
            updateBoundingBox(world, x, y, z);
            return base.raycast(world, x, y, z, startPos, endPos);
        }

        public override void onPlaced(World world, int x, int y, int z, int direction)
        {
            sbyte var6 = 0;
            if (direction == 2)
            {
                var6 = 0;
            }

            if (direction == 3)
            {
                var6 = 1;
            }

            if (direction == 4)
            {
                var6 = 2;
            }

            if (direction == 5)
            {
                var6 = 3;
            }

            world.setBlockMeta(x, y, z, var6);
        }

        public override bool canPlaceAt(World world, int x, int y, int z, int side)
        {
            if (side == 0)
            {
                return false;
            }
            else if (side == 1)
            {
                return false;
            }
            else
            {
                if (side == 2)
                {
                    ++z;
                }

                if (side == 3)
                {
                    --z;
                }

                if (side == 4)
                {
                    ++x;
                }

                if (side == 5)
                {
                    --x;
                }

                return world.shouldSuffocate(x, y, z);
            }
        }

        public static bool isOpen(int meta)
        {
            return (meta & 4) != 0;
        }
    }

}