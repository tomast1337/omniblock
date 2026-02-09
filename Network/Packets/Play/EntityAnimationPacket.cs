using betareborn.Entities;
using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class EntityAnimationPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityAnimationPacket).TypeHandle);

        public int entityId;
        public int animate;

        public EntityAnimationPacket()
        {
        }

        public EntityAnimationPacket(Entity var1, int var2)
        {
            entityId = var1.entityId;
            animate = var2;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            animate = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeByte(animate);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleArmAnimation(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}