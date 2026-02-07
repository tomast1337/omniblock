using java.io;

namespace betareborn.Packets
{
    public class Packet8UpdateHealth : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet8UpdateHealth).TypeHandle);

        public int healthMP;

        public override void read(DataInputStream var1)
        {
            this.healthMP = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(this.healthMP);
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