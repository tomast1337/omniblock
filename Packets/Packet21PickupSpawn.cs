using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet21PickupSpawn : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet21PickupSpawn).TypeHandle);

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

        public Packet21PickupSpawn()
        {
        }

        public Packet21PickupSpawn(EntityItem var1)
        {
            this.entityId = var1.entityId;
            this.itemID = var1.item.itemID;
            this.count = var1.item.count;
            this.itemDamage = var1.item.getItemDamage();
            this.xPosition = MathHelper.floor_double(var1.posX * 32.0D);
            this.yPosition = MathHelper.floor_double(var1.posY * 32.0D);
            this.zPosition = MathHelper.floor_double(var1.posZ * 32.0D);
            this.rotation = (sbyte)((int)(var1.motionX * 128.0D));
            this.pitch = (sbyte)((int)(var1.motionY * 128.0D));
            this.roll = (sbyte)((int)(var1.motionZ * 128.0D));
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.itemID = var1.readShort();
            this.count = (sbyte)var1.readByte();
            this.itemDamage = var1.readShort();
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.rotation = (sbyte)var1.readByte();
            this.pitch = (sbyte)var1.readByte();
            this.roll = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeShort(this.itemID);
            var1.writeByte(this.count);
            var1.writeShort(this.itemDamage);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.writeByte(this.rotation);
            var1.writeByte(this.pitch);
            var1.writeByte(this.roll);
        }

        public override void apply(NetHandler var1)
        {
            var1.handlePickupSpawn(this);
        }

        public override int size()
        {
            return 24;
        }
    }

}