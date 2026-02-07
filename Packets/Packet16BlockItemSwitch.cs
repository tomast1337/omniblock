using java.io;

namespace betareborn.Packets
{
    public class Packet16BlockItemSwitch : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet16BlockItemSwitch).TypeHandle);

        public int id;

        public Packet16BlockItemSwitch()
        {
        }

        public Packet16BlockItemSwitch(int var1)
        {
            this.id = var1;
        }

        public override void read(DataInputStream var1)
        {
            this.id = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(this.id);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleBlockItemSwitch(this);
        }

        public override int size()
        {
            return 2;
        }
    }

}