using java.io;

namespace betareborn.Packets
{
    public class Packet0KeepAlive : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet0KeepAlive).TypeHandle);

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