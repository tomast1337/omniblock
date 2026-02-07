using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet19EntityAction : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet19EntityAction).TypeHandle);

        public int entityId;
        public int state;

        public Packet19EntityAction()
        {
        }

        public Packet19EntityAction(Entity var1, int var2)
        {
            this.entityId = var1.entityId;
            this.state = var2;
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.state = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeByte(this.state);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_21147_a(this);
        }

        public override int size()
        {
            return 5;
        }
    }

}