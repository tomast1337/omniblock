using betareborn.Entities;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ItemEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ItemEntitySpawnS2CPacket).TypeHandle);

        public int id;
        public int x;
        public int y;
        public int z;
        public sbyte velocityX;
        public sbyte velocityY;
        public sbyte velocityZ;
        public int itemRawId;
        public int itemCount;
        public int itemDamage;

        public ItemEntitySpawnS2CPacket()
        {
        }

        public ItemEntitySpawnS2CPacket(EntityItem var1)
        {
            id = var1.id;
            itemRawId = var1.item.itemId;
            itemCount = var1.item.count;
            itemDamage = var1.item.getDamage();
            x = MathHelper.floor_double(var1.x * 32.0D);
            y = MathHelper.floor_double(var1.y * 32.0D);
            z = MathHelper.floor_double(var1.z * 32.0D);
            velocityX = (sbyte)(int)(var1.velocityX * 128.0D);
            velocityY = (sbyte)(int)(var1.velocityY * 128.0D);
            velocityZ = (sbyte)(int)(var1.velocityZ * 128.0D);
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            itemRawId = var1.readShort();
            itemCount = (sbyte)var1.readByte();
            itemDamage = var1.readShort();
            x = var1.readInt();
            y = var1.readInt();
            z = var1.readInt();
            velocityX = (sbyte)var1.readByte();
            velocityY = (sbyte)var1.readByte();
            velocityZ = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeShort(itemRawId);
            var1.writeByte(itemCount);
            var1.writeShort(itemDamage);
            var1.writeInt(x);
            var1.writeInt(y);
            var1.writeInt(z);
            var1.writeByte(velocityX);
            var1.writeByte(velocityY);
            var1.writeByte(velocityZ);
        }

        public override void apply(NetHandler var1)
        {
            var1.onItemEntitySpawn(this);
        }

        public override int size()
        {
            return 24;
        }
    }

}