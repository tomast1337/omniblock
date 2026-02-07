using java.io;

namespace betareborn.Packets
{
    public class Packet17Sleep : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet17Sleep).TypeHandle);

        public int field_22045_a;
        public int field_22044_b;
        public int field_22048_c;
        public int field_22047_d;
        public int field_22046_e;

        public override void read(DataInputStream var1)
        {
            this.field_22045_a = var1.readInt();
            this.field_22046_e = (sbyte)var1.readByte();
            this.field_22044_b = var1.readInt();
            this.field_22048_c = (sbyte)var1.readByte();
            this.field_22047_d = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.field_22045_a);
            var1.writeByte(this.field_22046_e);
            var1.writeInt(this.field_22044_b);
            var1.writeByte(this.field_22048_c);
            var1.writeInt(this.field_22047_d);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_22186_a(this);
        }

        public override int size()
        {
            return 14;
        }
    }

}