using betareborn.Entities;
using betareborn.Items;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class PlayerSpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerSpawnS2CPacket).TypeHandle);

        public int entityId;
        public string name;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte rotation;
        public sbyte pitch;
        public int currentItem;

        public PlayerSpawnS2CPacket()
        {
        }

        public PlayerSpawnS2CPacket(EntityPlayer var1)
        {
            entityId = var1.id;
            name = var1.name;
            xPosition = MathHelper.floor_double(var1.x * 32.0D);
            yPosition = MathHelper.floor_double(var1.y * 32.0D);
            zPosition = MathHelper.floor_double(var1.z * 32.0D);
            rotation = (sbyte)(int)(var1.yaw * 256.0F / 360.0F);
            pitch = (sbyte)(int)(var1.pitch * 256.0F / 360.0F);
            ItemStack var2 = var1.inventory.getSelectedItem();
            currentItem = var2 == null ? 0 : var2.itemId;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            name = readString(var1, 16);
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            rotation = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
            currentItem = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            writeString(name, var1);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.writeByte(rotation);
            var1.writeByte(pitch);
            var1.writeShort(currentItem);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayerSpawn(this);
        }

        public override int size()
        {
            return 28;
        }
    }

}