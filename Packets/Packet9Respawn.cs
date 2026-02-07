using java.io;

namespace betareborn.Packets
{
    public class Packet9Respawn : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet9Respawn).TypeHandle);

        public sbyte field_28048_a;

        public Packet9Respawn()
        {
        }

        public Packet9Respawn(sbyte var1)
        {
            this.field_28048_a = var1;
        }

        public override void apply(NetHandler var1)
        {
            var1.func_9448_a(this);
        }

        public override void read(DataInputStream var1)
        {
            this.field_28048_a = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(this.field_28048_a);
        }

        public override int size()
        {
            return 1;
        }
    }
}