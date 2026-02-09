using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerRespawnPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerRespawnPacket).TypeHandle);

        public sbyte field_28048_a;

        public PlayerRespawnPacket()
        {
        }

        public PlayerRespawnPacket(sbyte var1)
        {
            field_28048_a = var1;
        }

        public override void apply(NetHandler var1)
        {
            var1.func_9448_a(this);
        }

        public override void read(DataInputStream var1)
        {
            field_28048_a = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(field_28048_a);
        }

        public override int size()
        {
            return 1;
        }
    }
}