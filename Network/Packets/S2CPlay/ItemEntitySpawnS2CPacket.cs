using betareborn.Entities;
using betareborn.Network.Packets;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ItemEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ItemEntitySpawnS2CPacket).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte rotation;
        public sbyte pitch;
        public sbyte roll;
        public int itemID;
        public int count;
        public int itemDamage;

        public ItemEntitySpawnS2CPacket()
        {
        }

        public ItemEntitySpawnS2CPacket(EntityItem var1)
        {
            entityId = var1.id;
            itemID = var1.item.itemId;
            count = var1.item.count;
            itemDamage = var1.item.getDamage();
            xPosition = MathHelper.floor_double(var1.x * 32.0D);
            yPosition = MathHelper.floor_double(var1.y * 32.0D);
            zPosition = MathHelper.floor_double(var1.z * 32.0D);
            rotation = (sbyte)(int)(var1.velocityX * 128.0D);
            pitch = (sbyte)(int)(var1.velocityY * 128.0D);
            roll = (sbyte)(int)(var1.velocityZ * 128.0D);
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            itemID = var1.readShort();
            count = (sbyte)var1.readByte();
            itemDamage = var1.readShort();
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            rotation = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
            roll = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeShort(itemID);
            var1.writeByte(count);
            var1.writeShort(itemDamage);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.writeByte(rotation);
            var1.writeByte(pitch);
            var1.writeByte(roll);
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