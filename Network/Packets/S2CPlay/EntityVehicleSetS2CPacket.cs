using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityVehicleSetS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityVehicleSetS2CPacket).TypeHandle);

        public int entityId;
        public int vehicleEntityId;

        public override int size()
        {
            return 8;
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            vehicleEntityId = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeInt(vehicleEntityId);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_6497_a(this);
        }
    }

}