using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityStatusS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityStatusS2CPacket).TypeHandle);

        public int entityId;
        public sbyte entityStatus;

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            entityStatus = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(entityStatus);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_9447_a(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}