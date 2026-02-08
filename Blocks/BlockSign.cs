using betareborn.Items;
using betareborn.Worlds;
using java.lang;
using betareborn.Blocks.BlockEntities;
using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public class BlockSign : BlockWithEntity
    {
        //TODO: SIGNS ARE NOT BEING RENDERED?
        private Class blockEntityClazz;
        private bool standing;

        public BlockSign(int id, Class blockEntityClazz, bool standing) : base(id, Material.WOOD)
        {
            this.standing = standing;
            textureId = 4;
            this.blockEntityClazz = blockEntityClazz;
            float var4 = 0.25F;
            float var5 = 1.0F;
            setBoundingBox(0.5F - var4, 0.0F, 0.5F - var4, 0.5F + var4, var5, 0.5F + var4);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override Box getBoundingBox(World world, int x, int y, int z)
        {
            updateBoundingBox(world, x, y, z);
            return base.getBoundingBox(world, x, y, z);
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            if (!standing)
            {
                int var5 = blockView.getBlockMeta(x, y, z);
                float var6 = 9.0F / 32.0F;
                float var7 = 25.0F / 32.0F;
                float var8 = 0.0F;
                float var9 = 1.0F;
                float var10 = 2.0F / 16.0F;
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                if (var5 == 2)
                {
                    setBoundingBox(var8, var6, 1.0F - var10, var9, var7, 1.0F);
                }

                if (var5 == 3)
                {
                    setBoundingBox(var8, var6, 0.0F, var9, var7, var10);
                }

                if (var5 == 4)
                {
                    setBoundingBox(1.0F - var10, var6, var8, 1.0F, var7, var9);
                }

                if (var5 == 5)
                {
                    setBoundingBox(0.0F, var6, var8, var10, var7, var9);
                }

            }
        }

        public override int getRenderType()
        {
            return -1;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool isOpaque()
        {
            return false;
        }

        protected override BlockEntity getBlockEntity()
        {
            try
            {
                return (BlockEntity)blockEntityClazz.newInstance();
            }
            catch (java.lang.Exception var2)
            {
                throw new RuntimeException(var2);
            }
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.sign.id;
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            bool var6 = false;
            if (standing)
            {
                if (!world.getMaterial(x, y - 1, z).isSolid())
                {
                    var6 = true;
                }
            }
            else
            {
                int var7 = world.getBlockMeta(x, y, z);
                var6 = true;
                if (var7 == 2 && world.getMaterial(x, y, z + 1).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 3 && world.getMaterial(x, y, z - 1).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 4 && world.getMaterial(x + 1, y, z).isSolid())
                {
                    var6 = false;
                }

                if (var7 == 5 && world.getMaterial(x - 1, y, z).isSolid())
                {
                    var6 = false;
                }
            }

            if (var6)
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }

            base.neighborUpdate(world, x, y, z, id);
        }
    }

}