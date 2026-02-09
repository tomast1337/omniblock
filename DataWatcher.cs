using betareborn.Items;
using betareborn.Network.Packets;
using betareborn.Util.Maths;
using java.io;
using java.lang;
using java.util;

namespace betareborn
{
    public class DataWatcher : java.lang.Object
    {
        private static readonly HashMap dataTypes = [];
        private readonly Map watchedObjects = new HashMap();
        private bool objectChanged;

        public void addObject(int var1, java.lang.Object var2)
        {
            Integer var3 = (Integer)dataTypes.get(var2.GetType());
            if (var3 == null)
            {
                throw new ArgumentException("Unknown data type: " + var2.GetType());
            }
            else if (var1 > 31)
            {
                throw new ArgumentException("Data value id is too big with " + var1 + "! (Max is " + 31 + ")");
            }
            else if (watchedObjects.containsKey(Integer.valueOf(var1)))
            {
                throw new ArgumentException("Duplicate id value for " + var1 + "!");
            }
            else
            {
                WatchableObject var4 = new(var3.intValue(), var1, var2);
                watchedObjects.put(Integer.valueOf(var1), var4);
            }
        }

        public sbyte getWatchableObjectByte(int var1)
        {
            return (sbyte)((java.lang.Byte)((WatchableObject)watchedObjects.get(Integer.valueOf(var1))).getObject()).byteValue();
        }

        public int getWatchableObjectInt(int var1)
        {
            return ((Integer)((WatchableObject)watchedObjects.get(Integer.valueOf(var1))).getObject()).intValue();
        }

        public string getWatchableObjectString(int var1)
        {
            return ((JString)((WatchableObject)watchedObjects.get(Integer.valueOf(var1))).getObject()).value;
        }

        public void updateObject(int var1, java.lang.Object var2)
        {
            WatchableObject var3 = (WatchableObject)watchedObjects.get(Integer.valueOf(var1));
            if (!var2.Equals(var3.getObject()))
            {
                var3.setObject(var2);
                var3.setWatching(true);
                objectChanged = true;
            }
        }

        public static void writeObjectsInListToStream(List var0, DataOutputStream var1)
        {
            if (var0 != null)
            {
                Iterator var2 = var0.iterator();

                while (var2.hasNext())
                {
                    WatchableObject var3 = (WatchableObject)var2.next();
                    writeWatchableObject(var1, var3);
                }
            }

            var1.writeByte(127);
        }

        public void writeWatchableObjects(DataOutputStream var1)
        {
            Iterator var2 = watchedObjects.values().iterator();

            while (var2.hasNext())
            {
                WatchableObject var3 = (WatchableObject)var2.next();
                writeWatchableObject(var1, var3);
            }

            var1.writeByte(127);
        }

        private static void writeWatchableObject(DataOutputStream var0, WatchableObject var1)
        {
            int var2 = (var1.getObjectType() << 5 | var1.getDataValueId() & 31) & 255;
            var0.writeByte(var2);
            switch (var1.getObjectType())
            {
                case 0:
                    var0.writeByte(((java.lang.Byte)var1.getObject()).byteValue());
                    break;
                case 1:
                    var0.writeShort(((Short)var1.getObject()).shortValue());
                    break;
                case 2:
                    var0.writeInt(((Integer)var1.getObject()).intValue());
                    break;
                case 3:
                    var0.writeFloat(((Float)var1.getObject()).floatValue());
                    break;
                case 4:
                    Packet.writeString(((JString)var1.getObject()).value, var0);
                    break;
                case 5:
                    ItemStack var4 = (ItemStack)var1.getObject();
                    var0.writeShort(var4.getItem().id);
                    var0.writeByte(var4.count);
                    var0.writeShort(var4.getDamage());
                    break;
                case 6:
                    Vec3i var3 = (Vec3i)var1.getObject();
                    var0.writeInt(var3.x);
                    var0.writeInt(var3.y);
                    var0.writeInt(var3.z);
                    break;
            }
        }

        public static List readWatchableObjects(DataInputStream var0)
        {
            ArrayList var1 = null;

            for (sbyte var2 = (sbyte)var0.readByte(); var2 != 127; var2 = (sbyte)var0.readByte())
            {
                var1 ??= [];

                int var3 = (var2 & 224) >> 5;
                int var4 = var2 & 31;
                WatchableObject var5 = null;
                switch (var3)
                {
                    case 0:
                        var5 = new WatchableObject(var3, var4, java.lang.Byte.valueOf(var0.readByte()));
                        break;
                    case 1:
                        var5 = new WatchableObject(var3, var4, Short.valueOf(var0.readShort()));
                        break;
                    case 2:
                        var5 = new WatchableObject(var3, var4, Integer.valueOf(var0.readInt()));
                        break;
                    case 3:
                        var5 = new WatchableObject(var3, var4, Float.valueOf(var0.readFloat()));
                        break;
                    case 4:
                        var5 = new WatchableObject(var3, var4, new JString(Packet.readString(var0, 64)));
                        break;
                    case 5:
                        short var9 = var0.readShort();
                        sbyte var10 = (sbyte)var0.readByte();
                        short var11 = var0.readShort();
                        var5 = new WatchableObject(var3, var4, new ItemStack(var9, var10, var11));
                        break;
                    case 6:
                        int var6 = var0.readInt();
                        int var7 = var0.readInt();
                        int var8 = var0.readInt();
                        var5 = new WatchableObject(var3, var4, new Vec3i(var6, var7, var8));
                        break;
                }

                var1.add(var5);
            }

            return var1;
        }

        public void updateWatchedObjectsFromList(List var1)
        {
            Iterator var2 = var1.iterator();

            while (var2.hasNext())
            {
                WatchableObject var3 = (WatchableObject)var2.next();
                WatchableObject var4 = (WatchableObject)watchedObjects.get(Integer.valueOf(var3.getDataValueId()));
                if (var4 != null)
                {
                    var4.setObject(var3.getObject());
                }
            }
        }

        static DataWatcher()
        {
            dataTypes.put(typeof(java.lang.Byte), Integer.valueOf(0));
            dataTypes.put(typeof(Short), Integer.valueOf(1));
            dataTypes.put(typeof(Integer), Integer.valueOf(2));
            dataTypes.put(typeof(Float), Integer.valueOf(3));
            dataTypes.put(typeof(JString), Integer.valueOf(4));
            dataTypes.put(typeof(ItemStack), Integer.valueOf(5));
            dataTypes.put(typeof(Vec3i), Integer.valueOf(6));
        }
    }

}