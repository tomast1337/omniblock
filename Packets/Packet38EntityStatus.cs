using java.io;

namespace betareborn.Packets
{
    public class Packet38EntityStatus : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet38EntityStatus).TypeHandle);

        public int entityId;
        public sbyte entityStatus;

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.entityStatus = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeByte(this.entityStatus);
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