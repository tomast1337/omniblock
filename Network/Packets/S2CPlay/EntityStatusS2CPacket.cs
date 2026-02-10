using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityStatusS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityStatusS2CPacket).TypeHandle);

        public int entityId;
        public sbyte entityStatus;

        public EntityStatusS2CPacket()
        {
        }

        public EntityStatusS2CPacket(int entityId, byte status)
        {
            this.entityId = entityId;
            entityStatus = (sbyte)status;
        }

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
            var1.onEntityStatus(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}