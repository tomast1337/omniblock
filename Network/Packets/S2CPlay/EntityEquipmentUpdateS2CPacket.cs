using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityEquipmentUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityEquipmentUpdateS2CPacket).TypeHandle);

        public int entityID;
        public int slot;
        public int itemID;
        public int itemDamage;

        public override void read(DataInputStream var1)
        {
            entityID = var1.readInt();
            slot = var1.readShort();
            itemID = var1.readShort();
            itemDamage = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityID);
            var1.writeShort(slot);
            var1.writeShort(itemID);
            var1.writeShort(itemDamage);
        }

        public override void apply(NetHandler var1)
        {
            var1.handlePlayerInventory(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}