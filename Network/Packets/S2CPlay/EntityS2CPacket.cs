using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityS2CPacket).TypeHandle);

        public int entityId;
        public sbyte xPosition;
        public sbyte yPosition;
        public sbyte zPosition;
        public sbyte yaw;
        public sbyte pitch;
        public bool rotating = false;

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleEntity(this);
        }

        public override int size()
        {
            return 4;
        }
    }

}