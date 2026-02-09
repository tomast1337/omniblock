using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class IncreaseStatS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(IncreaseStatS2CPacket).TypeHandle);

        public int field_27052_a;
        public int field_27051_b;

        public override void apply(NetHandler var1)
        {
            var1.func_27245_a(this);
        }

        public override void read(DataInputStream var1)
        {
            field_27052_a = var1.readInt();
            field_27051_b = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(field_27052_a);
            var1.writeByte(field_27051_b);
        }

        public override int size()
        {
            return 6;
        }
    }

}