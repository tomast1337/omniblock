using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockCactus : Block
    {

        public BlockCactus(int id, int textureId) : base(id, textureId, Material.CACTUS)
        {
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

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            float var5 = 1.0F / 16.0F;
            return Box.createCached((double)((float)x + var5), (double)y, (double)((float)z + var5), (double)((float)(x + 1) - var5), (double)((float)(y + 1) - var5), (double)((float)(z + 1) - var5));
        }

        public override Box getBoundingBox(World world, int x, int y, int z)
        {
            float var5 = 1.0F / 16.0F;
            return Box.createCached((double)((float)x + var5), (double)y, (double)((float)z + var5), (double)((float)(x + 1) - var5), (double)(y + 1), (double)((float)(z + 1) - var5));
        }

        public override int getTexture(int side)
        {
            return side == 1 ? textureId - 1 : (side == 0 ? textureId + 1 : textureId);
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 13;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return !base.canPlaceAt(world, x, y, z) ? false : canGrow(world, x, y, z);
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (!canGrow(world, x, y, z))
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override bool canGrow(World world, int x, int y, int z)
        {
            if (world.getMaterial(x - 1, y, z).isSolid())
            {
                return false;
            }
            else if (world.getMaterial(x + 1, y, z).isSolid())
            {
                return false;
            }
            else if (world.getMaterial(x, y, z - 1).isSolid())
            {
                return false;
            }
            else if (world.getMaterial(x, y, z + 1).isSolid())
            {
                return false;
            }
            else
            {
                int var5 = world.getBlockId(x, y - 1, z);
                return var5 == Block.CACTUS.id || var5 == Block.SAND.id;
            }
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            entity.attackEntityFrom((Entity)null, 1);
        }
    }

}