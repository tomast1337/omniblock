using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ItemPickupAnimationS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ItemPickupAnimationS2CPacket).TypeHandle);

        public int collectedEntityId;
        public int collectorEntityId;

        public override void read(DataInputStream var1)
        {
            collectedEntityId = var1.readInt();
            collectorEntityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(collectedEntityId);
            var1.writeInt(collectorEntityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleCollect(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}