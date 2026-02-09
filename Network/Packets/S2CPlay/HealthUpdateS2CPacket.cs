using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class HealthUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(HealthUpdateS2CPacket).TypeHandle);

        public int healthMP;

        public override void read(DataInputStream var1)
        {
            healthMP = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(healthMP);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleHealth(this);
        }

        public override int size()
        {
            return 2;
        }
    }

}