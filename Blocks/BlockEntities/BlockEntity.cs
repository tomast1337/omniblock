using betareborn.NBT;
using betareborn.Packets;
using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.Blocks.BlockEntities
{
    public class BlockEntity : java.lang.Object
    {
        public static readonly Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(BlockEntity).TypeHandle);
        private static readonly Map idToClass = new HashMap();
        private static readonly Map classToId = new HashMap();
        public World world;
        public int x;
        public int y;
        public int z;
        protected bool removed;

        private static void create(Class blockEntityClass, string id)
        {
            if (classToId.containsKey(id))
            {
                throw new IllegalArgumentException("Duplicate id: " + id);
            }
            else
            {
                idToClass.put(id, blockEntityClass);
                classToId.put(blockEntityClass, id);
            }
        }

        public virtual void readNbt(NBTTagCompound nbt)
        {
            x = nbt.getInteger("x");
            y = nbt.getInteger("y");
            z = nbt.getInteger("z");
        }

        public virtual void writeNbt(NBTTagCompound nbt)
        {
            string var2 = (string)classToId.get(getClass());
            if (var2 == null)
            {
                throw new RuntimeException(getClass() + " is missing a mapping! This is a bug!");
            }
            else
            {
                nbt.setString("id", var2);
                nbt.setInteger("x", x);
                nbt.setInteger("y", y);
                nbt.setInteger("z", z);
            }
        }

        public virtual void tick()
        {
        }

        public static BlockEntity createFromNbt(NBTTagCompound nbt)
        {
            BlockEntity var1 = null;

            try
            {
                Class var2 = (Class)idToClass.get(nbt.getString("id"));
                if (var2 != null)
                {
                    var1 = (BlockEntity)var2.newInstance();
                }
            }
            catch (java.lang.Exception var3)
            {
                var3.printStackTrace();
            }

            if (var1 != null)
            {
                var1.readNbt(nbt);
            }
            else
            {
                java.lang.System.@out.println("Skipping TileEntity with id " + nbt.getString("id"));
            }

            return var1;
        }

        public virtual int getPushedBlockData()
        {
            return world.getBlockMeta(x, y, z);
        }

        public void markDirty()
        {
            if (world != null)
            {
                world.updateBlockEntity(x, y, z, this);
            }

        }

        public double distanceFrom(double x, double y, double z)
        {
            double var7 = this.x + 0.5D - x;
            double var9 = this.y + 0.5D - y;
            double var11 = this.z + 0.5D - z;
            return var7 * var7 + var9 * var9 + var11 * var11;
        }

        public Block getBlock()
        {
            return Block.BLOCKS[world.getBlockId(x, y, z)];
        }

        public virtual Packet createUpdatePacket()
        {
            return null;
        }

        public bool isRemoved()
        {
            return removed;
        }

        public void markRemoved()
        {
            removed = true;
        }

        public void cancelRemoval()
        {
            removed = false;
        }

        static BlockEntity()
        {
            create(new BlockEntityFurnace().getClass(), "Furnace");
            create(new BlockEntityChest().getClass(), "Chest");
            create(new BlockEntityRecordPlayer().getClass(), "RecordPlayer");
            create(new BlockEntityDispenser().getClass(), "Trap");
            create(new BlockEntitySign().getClass(), "Sign");
            create(new BlockEntityMobSpawner().getClass(), "MobSpawner");
            create(new BlockEntityNote().getClass(), "Music");
            create(new BlockEntityPiston().getClass(), "Piston");
        }
    }
}