using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityS2CPacket).TypeHandle);

        public int id;
        public sbyte deltaX;
        public sbyte deltaY;
        public sbyte deltaZ;
        public sbyte yaw;
        public sbyte pitch;
        public bool rotate = false;
        public EntityS2CPacket(int entityId)
        {
            id = entityId;
        }

        public EntityS2CPacket()
        {
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntity(this);
        }

        public override int size()
        {
            return 4;
        }
    }

}