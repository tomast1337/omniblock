using java.io;

namespace betareborn.Packets
{
    public class Packet4UpdateTime : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet4UpdateTime).TypeHandle);

        public long time;

        public override void read(DataInputStream var1)
        {
            this.time = var1.readLong();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeLong(this.time);
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