using java.io;

namespace betareborn.Packets
{

    public class Packet61DoorChange : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet61DoorChange).TypeHandle);

        public int field_28050_a;
        public int field_28049_b;
        public int field_28053_c;
        public int field_28052_d;
        public int field_28051_e;

        public override void read(DataInputStream var1)
        {
            this.field_28050_a = var1.readInt();
            this.field_28053_c = var1.readInt();
            this.field_28052_d = (sbyte)var1.readByte();
            this.field_28051_e = var1.readInt();
            this.field_28049_b = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.field_28050_a);
            var1.writeInt(this.field_28053_c);
            var1.writeByte(this.field_28052_d);
            var1.writeInt(this.field_28051_e);
            var1.writeInt(this.field_28049_b);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_28115_a(this);
        }

        public override int size()
        {
            return 20;
        }
    }

}