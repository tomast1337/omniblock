using java.io;

namespace betareborn.Packets
{
    public class Packet131MapData : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet131MapData).TypeHandle);

        public short field_28055_a;
        public short field_28054_b;
        public byte[] field_28056_c;

        public Packet131MapData()
        {
            this.worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            this.field_28055_a = var1.readShort();
            this.field_28054_b = var1.readShort();
            this.field_28056_c = new byte[(sbyte)var1.readByte() & 255];
            var1.readFully(this.field_28056_c);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(this.field_28055_a);
            var1.writeShort(this.field_28054_b);
            var1.writeByte(this.field_28056_c.Length);
            var1.write(this.field_28056_c);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_28116_a(this);
        }

        public override int size()
        {
            return 4 + this.field_28056_c.Length;
        }
    }

}