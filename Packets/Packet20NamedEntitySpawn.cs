using betareborn.Entities;
using betareborn.Items;
using java.io;

namespace betareborn.Packets
{
    public class Packet20NamedEntitySpawn : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet20NamedEntitySpawn).TypeHandle);

        public int entityId;
        public String name;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte rotation;
        public sbyte pitch;
        public int currentItem;

        public Packet20NamedEntitySpawn()
        {
        }

        public Packet20NamedEntitySpawn(EntityPlayer var1)
        {
            this.entityId = var1.entityId;
            this.name = var1.username;
            this.xPosition = MathHelper.floor_double(var1.posX * 32.0D);
            this.yPosition = MathHelper.floor_double(var1.posY * 32.0D);
            this.zPosition = MathHelper.floor_double(var1.posZ * 32.0D);
            this.rotation = (sbyte)((int)(var1.rotationYaw * 256.0F / 360.0F));
            this.pitch = (sbyte)((int)(var1.rotationPitch * 256.0F / 360.0F));
            ItemStack var2 = var1.inventory.getCurrentItem();
            this.currentItem = var2 == null ? 0 : var2.itemID;
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.name = readString(var1, 16);
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.rotation = (sbyte)var1.readByte();
            this.pitch = (sbyte)var1.readByte();
            this.currentItem = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            writeString(this.name, var1);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.writeByte(this.rotation);
            var1.writeByte(this.pitch);
            var1.writeShort(this.currentItem);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleNamedEntitySpawn(this);
        }

        public override int size()
        {
            return 28;
        }
    }

}