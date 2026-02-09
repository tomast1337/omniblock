using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class WorldTimeUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(WorldTimeUpdateS2CPacket).TypeHandle);

        public long time;

        public override void read(DataInputStream var1)
        {
            time = var1.readLong();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeLong(time);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleUpdateTime(this);
        }

        public override int size()
        {
            return 8;
        }
    }

}