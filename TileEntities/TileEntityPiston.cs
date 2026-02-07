using betareborn.Blocks;
using betareborn.Entities;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityPiston : TileEntity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntityPiston).TypeHandle);

        private int storedBlockID;
        private int storedMetadata;
        private int field_31025_c;
        private bool field_31024_i;
        private readonly bool field_31023_j;
        private float field_31022_k;
        private float field_31020_l;
        private static readonly List<Entity> field_31018_m = [];

        public TileEntityPiston()
        {
        }

        public TileEntityPiston(int var1, int var2, int var3, bool var4, bool var5)
        {
            storedBlockID = var1;
            storedMetadata = var2;
            field_31025_c = var3;
            field_31024_i = var4;
            field_31023_j = var5;
        }

        public int getStoredBlockID()
        {
            return storedBlockID;
        }

        public override int getPushedBlockData()
        {
            return storedMetadata;
        }

        public bool func_31015_b()
        {
            return field_31024_i;
        }

        public int func_31009_d()
        {
            return field_31025_c;
        }

        public bool func_31012_k()
        {
            return field_31023_j;
        }

        public float func_31008_a(float var1)
        {
            if (var1 > 1.0F)
            {
                var1 = 1.0F;
            }

            return field_31020_l + (field_31022_k - field_31020_l) * var1;
        }

        public float func_31017_b(float var1)
        {
            return field_31024_i ? (func_31008_a(var1) - 1.0F) * (float)PistonBlockTextures.field_31056_b[field_31025_c] : (1.0F - func_31008_a(var1)) * (float)PistonBlockTextures.field_31056_b[field_31025_c];
        }

        public float func_31014_c(float var1)
        {
            return field_31024_i ? (func_31008_a(var1) - 1.0F) * (float)PistonBlockTextures.field_31059_c[field_31025_c] : (1.0F - func_31008_a(var1)) * (float)PistonBlockTextures.field_31059_c[field_31025_c];
        }

        public float func_31013_d(float var1)
        {
            return field_31024_i ? (func_31008_a(var1) - 1.0F) * (float)PistonBlockTextures.field_31058_d[field_31025_c] : (1.0F - func_31008_a(var1)) * (float)PistonBlockTextures.field_31058_d[field_31025_c];
        }

        private void func_31010_a(float var1, float var2)
        {
            if (!field_31024_i)
            {
                --var1;
            }
            else
            {
                var1 = 1.0F - var1;
            }

            Box var3 = Block.pistonMoving.func_31035_a(world, x, y, z, storedBlockID, var1, field_31025_c);
            if (var3 != null)
            {
                var var4 = world.getEntitiesWithinAABBExcludingEntity((Entity)null, var3);
                if (var4.Count > 0)
                {
                    field_31018_m.AddRange(var4);
                    foreach (Entity var6 in field_31018_m)
                    {
                        var6.moveEntity(
                            (double)(var2 * (float)PistonBlockTextures.field_31056_b[field_31025_c]),
                            (double)(var2 * (float)PistonBlockTextures.field_31059_c[field_31025_c]),
                            (double)(var2 * (float)PistonBlockTextures.field_31058_d[field_31025_c])
                        );
                    }
                    field_31018_m.Clear();
                }
            }

        }

        public void func_31011_l()
        {
            if (field_31020_l < 1.0F)
            {
                field_31020_l = field_31022_k = 1.0F;
                world.removeBlockTileEntity(x, y, z);
                markRemoved();
                if (world.getBlockId(x, y, z) == Block.pistonMoving.blockID)
                {
                    world.setBlockAndMetadataWithNotify(x, y, z, storedBlockID, storedMetadata);
                }
            }

        }

        public override void tick()
        {
            field_31020_l = field_31022_k;
            if (field_31020_l >= 1.0F)
            {
                func_31010_a(1.0F, 0.25F);
                world.removeBlockTileEntity(x, y, z);
                markRemoved();
                if (world.getBlockId(x, y, z) == Block.pistonMoving.blockID)
                {
                    world.setBlockAndMetadataWithNotify(x, y, z, storedBlockID, storedMetadata);
                }

            }
            else
            {
                field_31022_k += 0.5F;
                if (field_31022_k >= 1.0F)
                {
                    field_31022_k = 1.0F;
                }

                if (field_31024_i)
                {
                    func_31010_a(field_31022_k, field_31022_k - field_31020_l + 1.0F / 16.0F);
                }

            }
        }

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            storedBlockID = var1.getInteger("blockId");
            storedMetadata = var1.getInteger("blockData");
            field_31025_c = var1.getInteger("facing");
            field_31020_l = field_31022_k = var1.getFloat("progress");
            field_31024_i = var1.getBoolean("extending");
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            var1.setInteger("blockId", storedBlockID);
            var1.setInteger("blockData", storedMetadata);
            var1.setInteger("facing", field_31025_c);
            var1.setFloat("progress", field_31020_l);
            var1.setBoolean("extending", field_31024_i);
        }
    }
}