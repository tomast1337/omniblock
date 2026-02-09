using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class GameStateChangeS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(GameStateChangeS2CPacket).TypeHandle);

        public static readonly string[] field_25020_a = new string[] { "tile.bed.notValid", null, null };
        public int field_25019_b;

        public override void read(DataInputStream var1)
        {
            field_25019_b = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(field_25019_b);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_25118_a(this);
        }

        public override int size()
        {
            return 1;
        }
    }

}