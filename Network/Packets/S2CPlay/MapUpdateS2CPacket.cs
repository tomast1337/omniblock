using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class MapUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(MapUpdateS2CPacket).TypeHandle);

        public short field_28055_a;
        public short field_28054_b;
        public byte[] field_28056_c;

        public MapUpdateS2CPacket()
        {
            worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            field_28055_a = var1.readShort();
            field_28054_b = var1.readShort();
            field_28056_c = new byte[(sbyte)var1.readByte() & 255];
            var1.readFully(field_28056_c);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(field_28055_a);
            var1.writeShort(field_28054_b);
            var1.writeByte(field_28056_c.Length);
            var1.write(field_28056_c);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_28116_a(this);
        }

        public override int size()
        {
            return 4 + field_28056_c.Length;
        }
    }

}