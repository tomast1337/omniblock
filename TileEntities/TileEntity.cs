using betareborn.Blocks;
using betareborn.NBT;
using betareborn.Packets;
using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.TileEntities
{
    public class TileEntity : java.lang.Object
    {
        public static readonly Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntity).TypeHandle);
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

        public static TileEntity createFromNbt(NBTTagCompound nbt)
        {
            TileEntity var1 = null;

            try
            {
                Class var2 = (Class)idToClass.get(nbt.getString("id"));
                if (var2 != null)
                {
                    var1 = (TileEntity)var2.newInstance();
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
            return world.getBlockMetadata(x, y, z);
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
            double var7 = (double)this.x + 0.5D - x;
            double var9 = (double)this.y + 0.5D - y;
            double var11 = (double)this.z + 0.5D - z;
            return var7 * var7 + var9 * var9 + var11 * var11;
        }

        public Block getBlock()
        {
            return Block.blocksList[world.getBlockId(x, y, z)];
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

        static TileEntity()
        {
            create(new TileEntityFurnace().getClass(), "Furnace");
            create(new TileEntityChest().getClass(), "Chest");
            create(new TileEntityRecordPlayer().getClass(), "RecordPlayer");
            create(new TileEntityDispenser().getClass(), "Trap");
            create(new TileEntitySign().getClass(), "Sign");
            create(new TileEntityMobSpawner().getClass(), "MobSpawner");
            create(new TileEntityNote().getClass(), "Music");
            create(new TileEntityPiston().getClass(), "Piston");
        }
    }
}