using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class ScreenHandlerAcknowledgementPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerAcknowledgementPacket).TypeHandle);

        public int windowId;
        public short field_20028_b;
        public bool field_20030_c;

        public ScreenHandlerAcknowledgementPacket()
        {
        }

        public ScreenHandlerAcknowledgementPacket(int var1, short var2, bool var3)
        {
            windowId = var1;
            field_20028_b = var2;
            field_20030_c = var3;
        }

        public override void apply(NetHandler var1)
        {
            var1.func_20089_a(this);
        }

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
            field_20028_b = var1.readShort();
            field_20030_c = (sbyte)var1.readByte() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
            var1.writeShort(field_20028_b);
            var1.writeByte(field_20030_c ? 1 : 0);
        }

        public override int size()
        {
            return 4;
        }
    }

}