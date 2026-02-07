using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet18Animation : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet18Animation).TypeHandle);

        public int entityId;
        public int animate;

        public Packet18Animation()
        {
        }

        public Packet18Animation(Entity var1, int var2)
        {
            this.entityId = var1.entityId;
            this.animate = var2;
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.animate = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeByte(this.animate);
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