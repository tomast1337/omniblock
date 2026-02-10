using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ItemPickupAnimationS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ItemPickupAnimationS2CPacket).TypeHandle);

        public int entityId;
        public int collectorEntityId;

        public ItemPickupAnimationS2CPacket()
        {
        }

        public ItemPickupAnimationS2CPacket(int entityId, int collectorId)
        {
            this.entityId = entityId;
            collectorEntityId = collectorId;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            collectorEntityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeInt(collectorEntityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.onItemPickupAnimation(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}