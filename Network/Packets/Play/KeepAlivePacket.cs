using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class KeepAlivePacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(KeepAlivePacket).TypeHandle);

        public override void apply(NetHandler var1)
        {
        }

        public override void read(DataInputStream var1)
        {
        }

        public override void write(DataOutputStream var1)
        {
        }

        public override int size()
        {
            return 0;
        }
    }

}