using java.io;

namespace betareborn.Packets
{
    public class Packet39AttachEntity : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet39AttachEntity).TypeHandle);

        public int entityId;
        public int vehicleEntityId;

        public override int size()
        {
            return 8;
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.vehicleEntityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeInt(this.vehicleEntityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_6497_a(this);
        }
    }

}