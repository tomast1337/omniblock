using betareborn.Items;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityEquipmentUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityEquipmentUpdateS2CPacket).TypeHandle);

        public int id;
        public int slot;
        public int itemRawId;
        public int itemDamage;

        public EntityEquipmentUpdateS2CPacket()
        {
        }

        public EntityEquipmentUpdateS2CPacket(int id, int slot, ItemStack itemStack)
        {
            this.id = id;
            this.slot = slot;
            if (itemStack == null)
            {
                itemRawId = -1;
                itemDamage = 0;
            }
            else
            {
                itemRawId = itemStack.itemId;
                itemDamage = itemStack.getDamage();
            }
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            slot = var1.readShort();
            itemRawId = var1.readShort();
            itemDamage = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeShort(slot);
            var1.writeShort(itemRawId);
            var1.writeShort(itemDamage);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityEquipmentUpdate(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}