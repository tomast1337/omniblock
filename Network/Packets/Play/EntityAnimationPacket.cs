using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class EntityAnimationPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityAnimationPacket).TypeHandle);

        public int id;
        public int animationId;

        public EntityAnimationPacket()
        {
        }

        public EntityAnimationPacket(Entity var1, int var2)
        {
            id = var1.id;
            animationId = var2;
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            animationId = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeByte(animationId);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityAnimation(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}