using java.io;

namespace betareborn.Packets
{
    public class Packet200Statistic : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet200Statistic).TypeHandle);

        public int field_27052_a;
        public int field_27051_b;

        public override void apply(NetHandler var1)
        {
            var1.func_27245_a(this);
        }

        public override void read(DataInputStream var1)
        {
            this.field_27052_a = var1.readInt();
            this.field_27051_b = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.field_27052_a);
            var1.writeByte(this.field_27051_b);
        }

        public override int size()
        {
            return 6;
        }
    }

}